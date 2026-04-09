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
    public class SqliteSaveLoader : SaveLoader, IBatchableSaveLoader, Injectable, Cleanable
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
        private bool _synchronousOff;
        private bool _fullMutex;
        private SQLiteConnection _connection;

        // Lazy-open: the connection is created on first use so that
        // Application.persistentDataPath is always ready when accessed.
        private SQLiteConnection Connection => _connection ??= OpenConnection();

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _path = Path.Combine(Application.persistentDataPath, arguments.FileName);
            _synchronousOff = arguments.SynchronousOff;
            _fullMutex = arguments.FullMutex;
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

        void IBatchableSaveLoader.BeginBatch() => Connection.BeginTransaction();
        void IBatchableSaveLoader.CommitBatch() => Connection.Commit();
        void IBatchableSaveLoader.RollbackBatch() => Connection.Rollback();

        public void Clear()
        {
            Clean();
            DeleteDatabaseFiles(_path);
            OnClear?.Invoke();
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create;
            if (_fullMutex)
                flags |= SQLiteOpenFlags.FullMutex;

            SQLiteConnection conn = new SQLiteConnection(_path, flags);

            // WAL mode avoids a rollback journal file and per-write fsync, giving significantly
            // better write throughput for the many small writes produced by per-key saves.
            conn.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");

            // synchronous=OFF removes WAL frame syncing entirely, reducing per-transaction
            // overhead from ~2ms to ~0.1ms. Risk: data may be lost on OS crash or power loss
            // (not on application crash — SQLite always rolls back uncommitted transactions).
            if (_synchronousOff)
                conn.ExecuteScalar<string>("PRAGMA synchronous=OFF;");

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

            /// <summary>
            /// When <c>true</c>, opens the connection with <c>SQLITE_OPEN_FULLMUTEX</c> so it
            /// is safe to use from multiple threads. Required when async saves are enabled.
            /// </summary>
            public bool FullMutex;

            /// <summary>
            /// When <c>true</c>, sets <c>PRAGMA synchronous=OFF</c>, removing WAL frame syncing.
            /// Reduces per-transaction overhead from ~2ms to ~0.1ms at the cost of potential
            /// data loss on OS crash or power failure (not on normal application exit or crash).
            /// </summary>
            public bool SynchronousOff;
        }
    }
}
