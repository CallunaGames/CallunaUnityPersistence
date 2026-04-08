using System;
using System.IO;
using Calluna.DI;
using SQLite;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// A <see cref="SaveLoader"/> backed by a local SQLite database.
    /// Each key-value pair is stored as a separate row, so only the rows that change
    /// need to be written on each save — unlike the file-based backend which rewrites
    /// the entire file every time.
    /// <para>
    /// Uses sqlite-net (MIT) included as source, and the native sqlite3 library.
    /// On Windows the native <c>sqlite3.dll</c> is bundled with this package.
    /// On macOS, Linux, iOS, and Android, sqlite3 is a system library — nothing extra to install.
    /// </para>
    /// <para><b>Platform note:</b> WebGL is not supported.</para>
    /// </summary>
    public class SqliteSaveLoader : SaveLoader, Injectable, Cleanable
    {
        [Table("entries")]
        private class Entry
        {
            [PrimaryKey, Column("key")]
            public string Key { get; set; }

            [Column("value")]
            public string Value { get; set; }
        }

        public event Action OnClear;

        private JsonSerializer _serializer;
        private string _path;
        private SQLiteConnection _connection;

        // Lazy-open: the connection is created on first use so that
        // Application.persistentDataPath is always ready when accessed.
        private SQLiteConnection Connection => _connection ??= OpenConnection();

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _path = Path.Combine(Application.persistentDataPath, arguments.FileName);
        }

        /// <summary>
        /// Closes the SQLite connection. Called automatically by the Calluna.DI system during cleanup.
        /// </summary>
        public void Clean()
        {
            _connection?.Close();
            _connection = null;
        }

        public bool Has(string id) => _connection != null && _connection.Find<Entry>(id) != null;

        public T Load<T>(string id, T defaultValue = default)
        {
            Entry entry = Connection.Find<Entry>(id);
            if (entry == null)
                return defaultValue;
            return _serializer.Deserialize<T>(entry.Value);
        }

        public void Save<T>(string id, T value)
        {
            string json = _serializer.Serialize(value);
            Connection.InsertOrReplace(new Entry { Key = id, Value = json });
        }

        public void Delete(string id)
        {
            Connection.Delete<Entry>(id);
        }

        public void Clear()
        {
            Clean();
            DeleteDatabaseFiles(_path);
            OnClear?.Invoke();
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection conn = new SQLiteConnection(_path);
            conn.CreateTable<Entry>();
            return conn;
        }

        private static void DeleteDatabaseFiles(string path)
        {
            // SQLite may create -wal and -shm journal files alongside the main database.
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }

        public class Arguments
        {
            public string FileName;
        }
    }
}
