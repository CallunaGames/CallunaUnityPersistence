using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    public class GameDataPersistence : Injectable, Initializable
    {
        public ReadonlyObservable<bool> LoadingFailed => _loadFailed;
        public ReadonlyObservable<bool> DataWasReset => _dataWasReset;

        /// <summary>
        /// Reserved key used to store the save data version in the underlying SaveLoader.
        /// Do not use this string as a DataSaveLoader.DataId.
        /// </summary>
        internal const string VersionKey = "__version__";

        // Only relevant when migrating pre-v1.6 save files that used the legacy single-blob format.
        private const int CurrentGameDataStructureVersion = 1;

        private int minSupportedVersion => _arguments.MinSupportedVersion;
        private int currentVersion => _arguments.CurrentVersion;

        private Arguments _arguments;
        private SaveLoader _saveLoader;
        private JsonSerializer _serializer;
        private GameDataWriter _writer;

        private Dictionary<string, IDataSaveLoader> _saveLoaders = new Dictionary<string, IDataSaveLoader>();
        private List<IGameDataMigrator> _migrators;
        private List<GameDataStructureMigrationStep> _structureSteps;
        private readonly Observable<bool> _loadFailed = new Observable<bool>(false);
        private readonly Observable<bool> _dataWasReset = new Observable<bool>(false);

        // Set when stored data below MinSupportedVersion was reset: the next saves then write every
        // loader, dirty or not. Otherwise a loader with dirty tracking would keep its stale stored
        // data, which the new version key would then declare current. Cleared by the next
        // successful Save().
        private bool _saveAllLoaders;

        // Null when backups are disabled.
        private GameDataBackupStore _backups;
        // Set by RestoreBackup: the loaded (possibly broken) state must not be saved over the restored
        // data. Cleared by the next Load().
        private bool _backupRestored;

        void Initializable.Initialize()
        {
            _loadFailed.Value = false;
            _dataWasReset.Value = false;
        }

        void Injectable.Inject(Resolver resolver)
        {
            _arguments = resolver.Resolve<Arguments>();
            _saveLoader = resolver.Resolve<SaveLoader>();
            _serializer = resolver.Resolve<JsonSerializer>();
            _writer = resolver.Resolve<GameDataWriter>();
            _backups = _arguments.Backups != null ? new GameDataBackupStore(_arguments.Backups) : null;

            // Structure migration steps are only used when reading the legacy single-blob format
            // produced by versions prior to v1.6. Add a new step here if the blob schema changes.
            _structureSteps = new List<GameDataStructureMigrationStep>
            {
                new GameDataStructureMigration_v0Tov1(_serializer),
            };
            _structureSteps.Sort((a, b) => a.TargetVersion.CompareTo(b.TargetVersion));
        }

        /// <summary>The outcome of the last <see cref="Load"/>, including what failed.</summary>
        public GameDataLoadResult LastLoadResult { get; private set; } = GameDataLoadResult.NotLoaded;

        /// <summary>
        /// Loads the saved GameData. Uses the default value of each DataSaveLoader if no data is found.
        /// Resets data if the stored version is below MinSupportedVersion.
        /// On first load after upgrading from v1.5 or earlier, automatically migrates the legacy
        /// single-blob format to the new per-key format transparently.
        /// <para>
        /// Loading fails - <see cref="LoadingFailed"/> becomes true and <see cref="LastLoadResult"/>
        /// tells why - if the data can't be read, a migrator throws or any DataSaveLoader throws. All
        /// DataSaveLoaders are still tried, so every failed one is reported. A failed load writes
        /// nothing: migrated data is only stored once every DataSaveLoader has loaded it.
        /// </para>
        /// </summary>
        public void Load()
        {
            _saveAllLoaders = false;
            _backupRestored = false;
            LastLoadResult = LoadData();
            if (LastLoadResult.Succeeded)
                return;

            _loadFailed.Value = true;
            Debug.LogError($"Failed to load game data '{_arguments.GameDataId}': {LastLoadResult}");
            foreach (Exception exception in LastLoadResult.Exceptions)
                Debug.LogException(exception);
        }

        /// <summary>
        /// Saves data from all dirty DataSaveLoaders synchronously on the calling thread.
        /// Loaders that report <c>IsDirty == false</c> are skipped.
        /// After a successful write all loaders are marked clean.
        /// <para>
        /// Waits for a background write started by <see cref="SaveAsync"/> first - otherwise that
        /// write, holding an older snapshot, could finish after this one and overwrite newer data.
        /// </para>
        /// </summary>
        public void Save()
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("Save was aborted since loading of the GameData failed initially");
                return;
            }
            if (IsBlockedByRestore("Save"))
                return;

            try
            {
                _writer.FlushPendingWrite();
                List<(string Id, JToken Data)> snapshot = SerializeDirtyLoaders();
                _writer.CommitToStorage(snapshot);

                foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                    saveLoader.MarkClean();
                _saveAllLoaders = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game data '{_arguments.GameDataId}'");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Saves data from all dirty DataSaveLoaders. Serialization runs on the calling thread
        /// (main thread) so that live game state is captured safely. The write to storage is
        /// dispatched to a background thread so the caller is not blocked by I/O.
        /// <para>
        /// If the underlying <see cref="SaveLoader"/> requires main-thread access (e.g.
        /// <see cref="PlayerPrefsSaveLoader"/>) or the platform does not support background
        /// threads (WebGL), this method falls back to a synchronous write and logs a one-time
        /// warning.
        /// </para>
        /// <para>
        /// <b>Dirty flags are not cleared</b> after an async save. Call <see cref="Save"/> to
        /// clear them — this happens automatically on DI cleanup when <c>Save Data On Clean</c>
        /// is enabled in <see cref="GameDataInstaller"/>.
        /// </para>
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that completes when the write finishes. Fire-and-forget is safe;
        /// await it only if you need to react to completion or errors.
        /// </returns>
        public Task SaveAsync()
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("SaveAsync was aborted since loading of the GameData failed initially");
                return Task.CompletedTask;
            }
            if (IsBlockedByRestore("SaveAsync"))
                return Task.CompletedTask;

            List<(string Id, JToken Data)> snapshot;
            try
            {
                snapshot = SerializeDirtyLoaders();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to serialize game data '{_arguments.GameDataId}'");
                Debug.LogException(e);
                return Task.CompletedTask;
            }

            return _writer.SaveAsync(snapshot);
        }

        /// <summary>
        /// Blocks the calling thread until any in-progress background write initiated by
        /// <see cref="SaveAsync"/> completes. Called automatically by the DI cleanup path
        /// before the final synchronous <see cref="Save"/>, ensuring no write is lost on
        /// scene teardown.
        /// </summary>
        public void FlushPendingWrite()
        {
            _writer.FlushPendingWrite();
            _backups?.FlushPendingWrite();
        }

        /// <summary>
        /// The backups that can be restored - save versions from MinSupportedVersion up to the current
        /// one - newest first. Empty if backups are disabled.
        /// </summary>
        public IReadOnlyList<GameDataBackup> GetBackups()
        {
            if (_backups == null)
                return Array.Empty<GameDataBackup>();
            return _backups.List()
                .Where(backup => backup.SaveVersion >= minSupportedVersion && backup.SaveVersion <= currentVersion)
                .ToList();
        }

        /// <summary>
        /// Replaces the stored game data with <paramref name="backup"/>. The replaced data is kept next to
        /// the save (e.g. <c>Lofelia.db.before-restore-&lt;time&gt;</c>) where the SaveLoader supports it.
        /// The backup itself stays unchanged, so it can be restored again if loading it fails.
        /// <para>
        /// The data in memory is the replaced one, so saving is blocked until the next <see cref="Load"/>:
        /// reload the game data (e.g. restart the game) right after restoring.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Backups are disabled.</exception>
        /// <exception cref="ArgumentException">The backup's save version can't be loaded by this game version.</exception>
        /// <exception cref="Exception">The backup can't be read or written - the stored data is then unchanged
        /// unless the SaveLoader already kept it aside.</exception>
        public void RestoreBackup(GameDataBackup backup)
        {
            if (_backups == null)
                throw new InvalidOperationException("Game data backups are disabled.");
            if (backup.SaveVersion < minSupportedVersion || backup.SaveVersion > currentVersion)
                throw new ArgumentException(
                    $"{backup} can't be loaded: supported save versions are {minSupportedVersion} to {currentVersion}.",
                    nameof(backup));

            FlushPendingWrite();
            Dictionary<string, JToken> data = _backups.ReadData(backup);

            if (_saveLoader is IArchivableSaveLoader archivable)
                archivable.Archive($".before-restore-{DateTime.Now:yyyyMMdd_HHmmss}");

            // Data the backup doesn't have must not survive from the replaced save.
            HashSet<string> toDelete = new HashSet<string>(_arguments.SaveLoaders.Select(loader => loader.DataId));
            toDelete.ExceptWith(data.Keys);
            List<(string Id, JToken Data)> entries = data.Select(pair => (pair.Key, pair.Value)).ToList();
            _writer.Commit(entries, toDelete, backup.SaveVersion);
            _backupRestored = true;
            Debug.Log($"Restored game data '{_arguments.GameDataId}' from {backup}.");
        }

        /// <summary>
        /// Saves custom data entries with a specific version. Use with caution!
        /// Each entry in <paramref name="data"/> is stored as its own key in the underlying SaveLoader,
        /// in a single batch. Waits for a background write started by <see cref="SaveAsync"/> first.
        /// </summary>
        public void OverrideSave(Dictionary<string, JToken> data, int version)
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("Override save was aborted since loading of the GameData failed initially");
                return;
            }
            if (IsBlockedByRestore("OverrideSave"))
                return;

            try
            {
                _writer.FlushPendingWrite();
                List<(string Id, JToken Data)> entries = new List<(string, JToken)>(data.Count);
                foreach (KeyValuePair<string, JToken> pair in data)
                    entries.Add((pair.Key, pair.Value));
                _writer.Commit(entries, null, version);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to perform override save of game data");
                Debug.LogException(e);
            }
        }

        // -----------------------------------------------------------------------
        // Private — serialization
        // -----------------------------------------------------------------------

        /// <summary>
        /// Serializes all dirty DataSaveLoaders into an in-memory snapshot.
        /// Must be called on the main thread. Throws if any serialization fails so that
        /// no partial snapshot is handed to the write phase.
        /// </summary>
        private List<(string Id, JToken Data)> SerializeDirtyLoaders()
        {
            List<(string Id, JToken Data)> result = null;
            foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
            {
                if (!saveLoader.IsDirty && !_saveAllLoaders) continue;
                JToken serialized = saveLoader.GetSerializedData();
                result ??= new List<(string, JToken)>(_saveLoaders.Count);
                result.Add((saveLoader.DataId, serialized));
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Private — load helpers
        // -----------------------------------------------------------------------

        private GameDataLoadResult LoadData()
        {
            try
            {
                InitMigrators();
                BuildSaveLoaderMap();
            }
            catch (Exception e)
            {
                return GameDataLoadResult.Failed(GameDataLoadFailure.Configuration, null, e);
            }

            int? storedVersion = null;
            Dictionary<string, JToken> loadedData;
            try
            {
                MigrateFromLegacyBlobIfNeeded();
                if (_saveLoader.Has(VersionKey))
                    storedVersion = _saveLoader.Load<int>(VersionKey, 0);
                loadedData = storedVersion >= minSupportedVersion
                    ? LoadAllData()
                    : new Dictionary<string, JToken>();
            }
            catch (Exception e)
            {
                return GameDataLoadResult.Failed(GameDataLoadFailure.Storage, storedVersion, e);
            }

            // No save yet, or one too old to migrate: every loader gets its default.
            if (storedVersion == null || storedVersion < minSupportedVersion)
            {
                if (storedVersion != null)
                {
                    Debug.LogWarning(
                        $"The loaded data version {storedVersion} is below the minimum supported version {minSupportedVersion}. The game data was reset.");
                    _dataWasReset.Value = true;
                    _saveAllLoaders = true;
                }
                return DispatchToLoaders(loadedData, storedVersion);
            }

            bool migrate = storedVersion < currentVersion;
            // The state before a migration is the last one the previous game version can load - backed
            // up as well, once the load succeeded.
            Dictionary<string, JToken> beforeMigration = migrate && _backups != null ? DeepClone(loadedData) : null;
            HashSet<string> removedByMigration = null;
            if (migrate)
            {
                try
                {
                    removedByMigration = new HashSet<string>(loadedData.Keys);
                    MigrateData(loadedData, storedVersion.Value);
                    removedByMigration.ExceptWith(loadedData.Keys);
                }
                catch (Exception e)
                {
                    return GameDataLoadResult.Failed(GameDataLoadFailure.Migration, storedVersion, e);
                }
            }

            GameDataLoadResult loaderResult = DispatchToLoaders(loadedData, storedVersion);
            if (!loaderResult.Succeeded)
                return loaderResult;

            if (migrate)
            {
                try
                {
                    // A slice a migrator removed must not stay in storage.
                    List<(string Id, JToken Data)> migrated = new List<(string, JToken)>(loadedData.Count);
                    foreach (KeyValuePair<string, JToken> pair in loadedData)
                        migrated.Add((pair.Key, pair.Value));
                    _writer.Commit(migrated, removedByMigration, currentVersion);
                }
                catch (Exception e)
                {
                    return GameDataLoadResult.Failed(GameDataLoadFailure.Storage, storedVersion, e);
                }
            }

            CreateBackups(loadedData, beforeMigration, storedVersion.Value);
            return GameDataLoadResult.Success(storedVersion);
        }

        // Only after a successful load: the data is known to be loadable. A failed load creates no
        // backup and removes none, so the existing ones stay until a load succeeds again.
        private void CreateBackups(Dictionary<string, JToken> loadedData, Dictionary<string, JToken> beforeMigration,
            int storedVersion)
        {
            if (_backups == null)
                return;
            try
            {
                List<(int, Dictionary<string, JToken>)> backups = new List<(int, Dictionary<string, JToken>)>(2)
                {
                    // Without a migration the data keeps its stored version (newer than the current
                    // one if an older game version loads a newer save).
                    (beforeMigration != null ? currentVersion : storedVersion, loadedData)
                };
                if (beforeMigration != null)
                    backups.Add((storedVersion, beforeMigration));
                _backups.CreateAsync(backups, currentVersion, Application.version, DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to back up game data '{_arguments.GameDataId}': {e}");
            }
        }

        private static Dictionary<string, JToken> DeepClone(Dictionary<string, JToken> data) =>
            data.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone());

        private bool IsBlockedByRestore(string operation)
        {
            if (!_backupRestored)
                return false;
            Debug.LogWarning($"{operation} was skipped: a backup was restored, reload the game data before saving.");
            return true;
        }

        // Tries every loader, so all failures are reported, not only the first.
        private GameDataLoadResult DispatchToLoaders(Dictionary<string, JToken> loadedData, int? storedVersion)
        {
            List<string> failedIds = null;
            List<Exception> exceptions = null;
            foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
            {
                try
                {
                    if (loadedData.TryGetValue(saveLoader.DataId, out JToken serializedData))
                        saveLoader.Load(serializedData);
                    else
                        saveLoader.LoadDefault();
                }
                catch (Exception e)
                {
                    (failedIds ??= new List<string>()).Add(saveLoader.DataId);
                    (exceptions ??= new List<Exception>()).Add(e);
                }
            }

            return failedIds == null
                ? GameDataLoadResult.Success(storedVersion)
                : new GameDataLoadResult(GameDataLoadFailure.DataSaveLoader, storedVersion, failedIds, exceptions);
        }

        private void BuildSaveLoaderMap()
        {
            _saveLoaders = new Dictionary<string, IDataSaveLoader>(_arguments.SaveLoaders.Count);
            foreach (IDataSaveLoader loader in _arguments.SaveLoaders)
            {
                if (!_saveLoaders.TryAdd(loader.DataId, loader))
                    throw new InvalidOperationException(
                        $"Duplicate DataSaveLoader id '{loader.DataId}'. Each loader must have a unique DataId.");
            }
        }

        private Dictionary<string, JToken> LoadAllData()
        {
            Dictionary<string, JToken> result = new Dictionary<string, JToken>(_saveLoaders.Count);
            foreach (IDataSaveLoader loader in _saveLoaders.Values)
            {
                JToken token = _saveLoader.Load<JToken>(loader.DataId, null);
                if (token != null)
                    result[loader.DataId] = token;
            }
            return result;
        }

        /// <summary>
        /// Detects and converts the legacy single-blob GameData format (used in v1.5 and earlier)
        /// to the new per-key format. Runs at most once per installation — after conversion the
        /// VersionKey is present and this method becomes a fast no-op.
        /// </summary>
        private void MigrateFromLegacyBlobIfNeeded()
        {
            if (_saveLoader.Has(VersionKey))
                return;

            JObject rawData = _saveLoader.Load<JObject>(_arguments.GameDataId, null);
            if (rawData == null)
                return;

            int structureVersion = rawData["StructureVersion"]?.Value<int>() ?? 0;
            GameData data;
            if (structureVersion < CurrentGameDataStructureVersion)
            {
                string json = _serializer.Serialize(rawData);
                foreach (GameDataStructureMigrationStep step in _structureSteps)
                {
                    if (structureVersion < step.TargetVersion && step.TargetVersion <= CurrentGameDataStructureVersion)
                        json = step.Migrate(json);
                }
                data = _serializer.Deserialize<GameData>(json);
            }
            else
            {
                data = _serializer.Deserialize<GameData>(rawData);
            }

            if (data.Entries != null)
            {
                foreach (GameDataEntry entry in data.Entries)
                    _saveLoader.Save(entry.Id, entry.Payload);
            }

            _saveLoader.Save(VersionKey, data.Version);
            _saveLoader.Delete(_arguments.GameDataId);
        }

        private void InitMigrators()
        {
            if (_arguments.Migrators == null || _arguments.Migrators.Count == 0)
            {
                _migrators = new List<IGameDataMigrator>(0);
                return;
            }

            ValidateVersions(_arguments.Migrators);
            _migrators = new List<IGameDataMigrator>(_arguments.Migrators);
            _migrators.Sort((a, b) => a.Version.CompareTo(b.Version));
        }

        private void MigrateData(Dictionary<string, JToken> loadedData, int dataVersion)
        {
            foreach (IGameDataMigrator migrator in _migrators)
            {
                if (dataVersion < migrator.Version && migrator.Version <= currentVersion)
                    migrator.Migrate(loadedData);
            }
        }

        private static void ValidateVersions(IReadOnlyList<IGameDataMigrator> migrators)
        {
            HashSet<int> versions = new HashSet<int>(migrators.Count);
            foreach (IGameDataMigrator migrator in migrators)
            {
                if (migrator.Version <= 0)
                    throw new ArgumentException("Game data migrator version is invalid. It must be greater than 0.");
                if (!versions.Add(migrator.Version))
                    throw new ArgumentException("Game data migrator has already been registered with version " +
                                                migrator.Version);
            }
        }

        internal class Arguments
        {
            /// <summary>
            /// Key used to detect and migrate legacy single-blob save data (v1.5 and earlier).
            /// Must match the value that was configured in GameDataInstaller before upgrading.
            /// </summary>
            public string GameDataId;
            public int MinSupportedVersion;
            public int CurrentVersion;
            public IReadOnlyList<IGameDataMigrator> Migrators;
            public IReadOnlyList<IDataSaveLoader> SaveLoaders;

            /// <summary>Null disables backups.</summary>
            public GameDataBackupStore.Settings Backups;
        }
    }
}
