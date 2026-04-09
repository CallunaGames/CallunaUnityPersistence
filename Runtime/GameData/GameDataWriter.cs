using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Handles all write operations for <see cref="GameDataPersistence"/>: synchronous commits,
    /// async background writes, and the pending-write flush used during DI cleanup.
    /// <para>
    /// Extracted from <see cref="GameDataPersistence"/> so that load orchestration and write
    /// mechanics live in separate classes.
    /// </para>
    /// </summary>
    internal class GameDataWriter : Injectable
    {
        private SaveLoader _saveLoader;
        private string _gameDataId;
        private int _currentVersion;

        private readonly object _writeLock = new object();
        private Task _pendingWrite;
        private List<(string Id, JToken Data)> _pendingSnapshot;
        private bool _mainThreadFallbackWarningLogged;

        private bool RequiresMainThread =>
            _saveLoader is IMainThreadSaveLoader ||
            Application.platform == RuntimePlatform.WebGLPlayer;

        void Injectable.Inject(Resolver resolver)
        {
            _saveLoader = resolver.Resolve<SaveLoader>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _gameDataId = arguments.GameDataId;
            _currentVersion = arguments.CurrentVersion;
        }

        /// <summary>
        /// Writes a serialized snapshot to the underlying <see cref="SaveLoader"/> on the
        /// calling thread. Wraps all writes in a single batch transaction when the backend
        /// supports it so that only one disk sync is needed per save round.
        /// </summary>
        internal void CommitToStorage(List<(string Id, JToken Data)> toWrite)
        {
            if (_saveLoader is IBatchableSaveLoader batchable)
            {
                try
                {
                    batchable.BeginBatch();
                    WriteEntries(toWrite);
                    batchable.CommitBatch();
                }
                catch
                {
                    batchable.RollbackBatch();
                    throw;
                }
            }
            else
            {
                WriteEntries(toWrite);
            }
        }

        /// <summary>
        /// Dispatches a pre-serialized snapshot to a background thread. If the backend requires
        /// main-thread access (e.g. <see cref="PlayerPrefsSaveLoader"/>) or the platform does not
        /// support background threads (WebGL), falls back to a synchronous write with a one-time
        /// warning.
        /// </summary>
        internal Task SaveAsync(List<(string Id, JToken Data)> snapshot)
        {
            if (RequiresMainThread)
            {
                if (!_mainThreadFallbackWarningLogged)
                {
                    Debug.LogWarning(
                        "GameDataPersistence.SaveAsync: the active SaveLoader requires main-thread " +
                        "access. Falling back to a synchronous write. This warning is logged once.");
                    _mainThreadFallbackWarningLogged = true;
                }

                CommitToStorage(snapshot);
                return Task.CompletedTask;
            }

            // "Latest wins" concurrency policy: if a write is in progress, store this snapshot
            // so it is committed immediately after the current write completes, replacing any
            // previously queued snapshot.
            lock (_writeLock)
            {
                if (_pendingWrite != null && !_pendingWrite.IsCompleted)
                {
                    _pendingSnapshot = snapshot;
                    return _pendingWrite;
                }

                _pendingWrite = Task.Run(() => WriteSnapshotBackground(snapshot));
                return _pendingWrite;
            }
        }

        /// <summary>
        /// Blocks the calling thread until any in-progress background write completes.
        /// </summary>
        internal void FlushPendingWrite()
        {
            Task pending;
            lock (_writeLock)
            {
                pending = _pendingWrite;
            }
            pending?.GetAwaiter().GetResult();
        }

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------

        private void WriteEntries(List<(string Id, JToken Data)> toWrite)
        {
            if (toWrite != null)
                foreach ((string id, JToken data) in toWrite)
                    _saveLoader.Save(id, data);
            _saveLoader.Save(GameDataPersistence.VersionKey, _currentVersion);
        }

        private void WriteSnapshotBackground(List<(string Id, JToken Data)> snapshot)
        {
            try
            {
                CommitToStorage(snapshot);
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveAsync failed for game data '{_gameDataId}'");
                Debug.LogException(e);
            }

            // If a newer snapshot arrived while we were writing, commit it now.
            List<(string Id, JToken Data)> next;
            lock (_writeLock)
            {
                next = _pendingSnapshot;
                _pendingSnapshot = null;
            }

            if (next != null)
                WriteSnapshotBackground(next);
        }

        internal class Arguments
        {
            public string GameDataId;
            public int CurrentVersion;
        }
    }
}
