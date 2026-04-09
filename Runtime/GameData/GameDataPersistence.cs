using System;
using System.Collections.Generic;
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
        private readonly Observable<bool> _loadFailed = false;
        private readonly Observable<bool> _dataWasReset = false;

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

            // Structure migration steps are only used when reading the legacy single-blob format
            // produced by versions prior to v1.6. Add a new step here if the blob schema changes.
            _structureSteps = new List<GameDataStructureMigrationStep>
            {
                new GameDataStructureMigration_v0Tov1(_serializer),
            };
            _structureSteps.Sort((a, b) => a.TargetVersion.CompareTo(b.TargetVersion));
        }

        /// <summary>
        /// Loads the saved GameData. Uses the default value of each DataSaveLoader if no data is found.
        /// Resets data if the stored version is below MinSupportedVersion.
        /// On first load after upgrading from v1.5 or earlier, automatically migrates the legacy
        /// single-blob format to the new per-key format transparently.
        /// </summary>
        public void Load()
        {
            try
            {
                InitMigrators();
                BuildSaveLoaderMap();

                MigrateFromLegacyBlobIfNeeded();

                if (!_saveLoader.Has(VersionKey))
                {
                    foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                        saveLoader.LoadDefault();
                    return;
                }

                int storedVersion = _saveLoader.Load<int>(VersionKey, 0);

                if (storedVersion < minSupportedVersion)
                {
                    Debug.LogWarning(
                        $"The loaded data version {storedVersion} is below the minimum supported version {minSupportedVersion}. The game data was reset.");
                    _dataWasReset.Value = true;
                    foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                        saveLoader.LoadDefault();
                    return;
                }

                Dictionary<string, JToken> loadedData = LoadAllData();

                if (storedVersion < currentVersion)
                {
                    MigrateData(loadedData, storedVersion);
                    foreach (KeyValuePair<string, JToken> pair in loadedData)
                        _saveLoader.Save(pair.Key, pair.Value);
                    _saveLoader.Save(VersionKey, currentVersion);
                }

                foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                    TryLoadData(saveLoader, loadedData);
            }
            catch (Exception e)
            {
                _loadFailed.Value = true;
                Debug.LogError($"Failed to load game data '{_arguments.GameDataId}'");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Saves data from all dirty DataSaveLoaders synchronously on the calling thread.
        /// Loaders that report <c>IsDirty == false</c> are skipped.
        /// After a successful write all loaders are marked clean.
        /// </summary>
        public void Save()
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("Save was aborted since loading of the GameData failed initially");
                return;
            }

            try
            {
                List<(string Id, JToken Data)> snapshot = SerializeDirtyLoaders();
                _writer.CommitToStorage(snapshot);

                foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                    saveLoader.MarkClean();
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
        }

        /// <summary>
        /// Saves custom data entries with a specific version. Use with caution!
        /// Each entry in <paramref name="data"/> is stored as its own key in the underlying SaveLoader.
        /// </summary>
        public void OverrideSave(Dictionary<string, JToken> data, int version)
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("Override save was aborted since loading of the GameData failed initially");
                return;
            }

            try
            {
                foreach (KeyValuePair<string, JToken> pair in data)
                    _saveLoader.Save(pair.Key, pair.Value);
                _saveLoader.Save(VersionKey, version);
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
                if (!saveLoader.IsDirty) continue;
                JToken serialized = saveLoader.GetSerializedData();
                result ??= new List<(string, JToken)>(_saveLoaders.Count);
                result.Add((saveLoader.DataId, serialized));
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Private — load helpers
        // -----------------------------------------------------------------------

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

        private void TryLoadData(IDataSaveLoader saveLoader, Dictionary<string, JToken> loadedData)
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
                Debug.LogError($"Failed to load data for '{saveLoader.DataId}'. Loading default instead.");
                Debug.LogException(e);
                saveLoader.LoadDefault();
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

        public class Arguments
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
        }
    }
}
