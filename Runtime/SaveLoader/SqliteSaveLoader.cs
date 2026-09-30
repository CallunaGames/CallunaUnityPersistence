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
    /// <para>
    /// The connection is opened on first use and closed by <see cref="Clean"/>. Operations after
    /// <see cref="Clean"/> - e.g. a final save that the DI cleanup runs after this loader's cleanup,
    /// since that order isn't guaranteed - still work, but on a short-lived connection that is closed
    /// right away, so no connection is left open after the loader was cleaned up.
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
        private bool _isCleaned;
        // A batch begun after Clean() runs on its own connection, closed when the batch ends.
        private SQLiteConnection _batchConnection;

        // Guards the connection's lifetime: regular operations take a read lock (so they can
        // still run concurrently with each other), while Clean() takes a write lock so it can
        // only close the connection once every in-flight operation - including ones dispatched
        // to a background thread via GameDataWriter.SaveAsync() - has finished. Without this,
        // DI cleanup order (which is not guaranteed) can close the connection while a background
        // write is still using it, producing a "bad parameter or other API misuse" SQLiteException.
        // A batch holds its read lock from BeginBatch until CommitBatch/RollbackBatch, so the
        // connection can't be closed in the middle of a transaction; recursion is allowed for the
        // operations inside the batch.
        // Internal (rather than private) so tests can hold/release it directly to deterministically
        // simulate an in-flight operation, the same way BlockingSaveLoader does in GameDataWriterTests.
        internal readonly ReaderWriterLockSlim _connectionLock = new(LockRecursionPolicy.SupportsRecursion);

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _path = Path.Combine(Application.persistentDataPath, arguments.FileName);
            _synchronousOff = arguments.SynchronousOff;
            _fullMutex = arguments.FullMutex;
        }

        // Internal (not a const/readonly) so tests can shrink it to keep the timeout test fast.
        internal static TimeSpan CleanLockTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Closes the SQLite connection. Called automatically by the Calluna.DI system during cleanup.
        /// Later operations run on short-lived connections (see the class remarks).
        /// </summary>
        public void Clean()
        {
            CloseConnection();
            _isCleaned = true;
        }

        public bool Has(string id)
        {
            _connectionLock.EnterReadLock();
            try
            {
                // Don't create the database just to answer that it has no data.
                if (_connection == null && _batchConnection == null && !File.Exists(_path))
                    return false;
                return Run(connection => connection.Find<Entry>(id) != null);
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        public T Load<T>(string id, T defaultValue = default)
        {
            Entry entry = RunLocked(connection => connection.Find<Entry>(id));
            return entry == null ? defaultValue : _serializer.Deserialize<T>(entry.Value);
        }

        public void Save<T>(string id, T value)
        {
            string json = _serializer.Serialize(value);
            RunLocked(connection => connection.InsertOrReplace(new Entry { Key = id, Value = json }));
        }

        public void Delete(string id)
        {
            RunLocked(connection => connection.Delete<Entry>(id));
        }

        void IBatchableSaveLoader.BeginBatch()
        {
            // Held until CommitBatch/RollbackBatch - see _connectionLock.
            _connectionLock.EnterReadLock();
            try
            {
                if (_isCleaned)
                {
                    _batchConnection = OpenConnection();
                    _batchConnection.BeginTransaction();
                }
                else
                {
                    Connection.BeginTransaction();
                }
            }
            catch
            {
                CloseBatchConnection();
                _connectionLock.ExitReadLock();
                throw;
            }
        }

        void IBatchableSaveLoader.CommitBatch()
        {
            try
            {
                (_batchConnection ?? Connection).Commit();
            }
            finally
            {
                EndBatch();
            }
        }

        void IBatchableSaveLoader.RollbackBatch()
        {
            try
            {
                (_batchConnection ?? Connection).Rollback();
            }
            finally
            {
                EndBatch();
            }
        }

        public void Clear()
        {
            CloseConnection();
            DeleteDatabaseFiles(_path);
            OnClear?.Invoke();
        }

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------

        // Lazy-open: the connection is created on first use so that
        // Application.persistentDataPath is always ready when accessed.
        private SQLiteConnection Connection => _connection ??= OpenConnection();

        private TResult RunLocked<TResult>(Func<SQLiteConnection, TResult> operation)
        {
            _connectionLock.EnterReadLock();
            try
            {
                return Run(operation);
            }
            finally
            {
                _connectionLock.ExitReadLock();
            }
        }

        // Must be called with the read lock held.
        private TResult Run<TResult>(Func<SQLiteConnection, TResult> operation)
        {
            if (_batchConnection != null)
                return operation(_batchConnection);
            if (!_isCleaned)
                return operation(Connection);
            using SQLiteConnection shortLived = OpenConnection();
            return operation(shortLived);
        }

        private void EndBatch()
        {
            CloseBatchConnection();
            // Commit may already have released it if it threw and RollbackBatch followed.
            if (_connectionLock.IsReadLockHeld)
                _connectionLock.ExitReadLock();
        }

        private void CloseBatchConnection()
        {
            _batchConnection?.Close();
            _batchConnection = null;
        }

        private void CloseConnection()
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
