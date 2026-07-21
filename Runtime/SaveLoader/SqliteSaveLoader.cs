using System;
using System.IO;
using System.Threading;
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

        // Guards the connection's lifetime: regular operations take a read lock (so they can
        // still run concurrently with each other), while Clean() takes a write lock so it can
        // only close the connection once every in-flight operation - including ones dispatched
        // to a background thread via GameDataWriter.SaveAsync() - has finished. Without this,
        // DI cleanup order (which is not guaranteed) can close the connection while a background
        // write is still using it, producing a "bad parameter or other API misuse" SQLiteException.
        private readonly ReaderWriterLockSlim _connectionLock = new();

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
        private static readonly TimeSpan CleanLockTimeout = TimeSpan.FromSeconds(5);

        public void Clean()
        {
            // Waits for any in-flight operation (including a background SaveAsync write) to
            // finish before closing. Bounded so a stuck disk op can't hang the app on quit
            // forever - if that happens the connection is force-closed anyway and a warning
            // is logged, since leaving quit unable to complete is worse than a rare misuse error.
            bool acquired = _connectionLock.TryEnterWriteLock(CleanLockTimeout);
            try
            {
                if (!acquired)
                    Debug.LogWarning(
                        "SqliteSaveLoader.Clean: timed out waiting for in-flight operations to " +
                        "finish. Closing the connection anyway.");
                _connection?.Close();
                _connection = null;
            }
            finally
            {
                if (acquired)
                    _connectionLock.ExitWriteLock();
            }
        }

        public bool Has(string id)
        {
            _connectionLock.EnterReadLock();
            try
            {
                return _connection != null && _connection.Find<Entry>(id) != null;
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        public T Load<T>(string id, T defaultValue = default)
        {
            _connectionLock.EnterReadLock();
            try
            {
                Entry entry = Connection.Find<Entry>(id);
                if (entry == null)
                    return defaultValue;
                return _serializer.Deserialize<T>(entry.Value);
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        public void Save<T>(string id, T value)
        {
            _connectionLock.EnterReadLock();
            try
            {
                string json = _serializer.Serialize(value);
                Connection.InsertOrReplace(new Entry { Key = id, Value = json });
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        public void Delete(string id)
        {
            _connectionLock.EnterReadLock();
            try
            {
                Connection.Delete<Entry>(id);
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        void IBatchableSaveLoader.BeginBatch()
        {
            _connectionLock.EnterReadLock();
            try
            {
                Connection.BeginTransaction();
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        void IBatchableSaveLoader.CommitBatch()
        {
            _connectionLock.EnterReadLock();
            try
            {
                Connection.Commit();
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        void IBatchableSaveLoader.RollbackBatch()
        {
            _connectionLock.EnterReadLock();
            try
            {
                Connection.Rollback();
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

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

        internal class Arguments
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
