using System;
using System.Collections.Generic;
using System.Linq;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    public class GameDataPersistence : Injectable, Initializable
    {
        public ReadonlyObservable<bool> LoadingFailed => _loadFailed;
        public ReadonlyObservable<bool> DataWasReset => _dataWasReset;

        // Increment this constant and add a new GameDataStructureMigrationStep when GameData's class shape changes.
        private const int CurrentGameDataStructureVersion = 1;

        private int minSupportedVersion => _arguments.MinSupportedVersion;
        private int currentVersion => _arguments.CurrentVersion;

        private Arguments _arguments;
        private SaveLoader _saveLoader;
        private JsonSerializer _serializer;

        private Dictionary<string, IDataSaveLoader> _saveLoaders = new Dictionary<string, IDataSaveLoader>();
        private Dictionary<string, JToken> _loadedData = new Dictionary<string, JToken>();
        private List<IGameDataMigrator> _migrators;
        private List<GameDataStructureMigrationStep> _structureSteps;
        private readonly Observable<bool> _loadFailed = false;
        private readonly Observable<bool> _dataWasReset = false;
        private Dictionary<string, JToken> _collectedData;
        private GameData _gameData;

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

            // Add new GameDataStructureMigrationStep entries here when GameData's class shape changes.
            _structureSteps = new List<GameDataStructureMigrationStep>
            {
                new GameDataStructureMigration_v0Tov1(_serializer),
            };
            _structureSteps.Sort((a, b) => a.TargetVersion.CompareTo(b.TargetVersion));
        }

        /// <summary>
        /// Loads the saved GameData. Uses the default value of each data if no GameData is found.
        /// Resets GameData if the data version is not supported anymore
        /// </summary>
        public void Load()
        {
            try
            {
                InitMigrators();
                _gameData = LoadGameData();
                _loadedData = _gameData.Entries.ToDictionary(d => d.Id, d => d.Payload);

                MigrateData(_loadedData, _gameData.Version);

                _saveLoaders = new Dictionary<string, IDataSaveLoader>(_arguments.SaveLoaders.Count);
                foreach (IDataSaveLoader loader in _arguments.SaveLoaders)
                {
                    if (!_saveLoaders.TryAdd(loader.DataId, loader))
                        throw new InvalidOperationException(
                            $"Duplicate DataSaveLoader id '{loader.DataId}'. Each loader must have a unique DataId.");
                }
                foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
                    TryLoadData(saveLoader);
            }
            catch (Exception e)
            {
                _loadFailed.Value = true;
                Debug.LogError($"Failed to load game data '{_arguments.GameDataId}'");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Saves the game data by collecting data from all provided DataSaveLoaders using the highest version of the data migrators
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
                CollectSaveData();
                SaveGameData(_collectedData, currentVersion);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game data '{_arguments.GameDataId}'");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Saves game data with custom data dictionary and version. Use with caution!
        /// </summary>
        /// <param name="data">The serialized key value pairs (Data Id, Serialized Data)</param>
        /// <param name="version">The custom version of this save data</param>
        public void OverrideSave(Dictionary<string, JToken> data, int version)
        {
            if (_loadFailed.Value)
            {
                Debug.LogError("Override save was aborted since loading of the GameData failed initially");
                return;
            }

            try
            {
                SaveGameData(data, version);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to perform override save of game data");
                Debug.LogException(e);
            }
        }

        private GameData LoadGameData()
        {
            JObject rawData = _saveLoader.Load<JObject>(_arguments.GameDataId, null);
            if (rawData == null)
                return CreateDefaultGameData();

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
                data.StructureVersion = CurrentGameDataStructureVersion;
            }
            else
            {
                data = _serializer.Deserialize<GameData>(rawData);
            }

            if (data.Version >= minSupportedVersion)
                return data;
            Debug.LogWarning(
                $"The loaded data version {data.Version} is below the minimum supported version {minSupportedVersion}. The game data was reset.");
            _dataWasReset.Value = true;
            return CreateDefaultGameData();
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
            if (dataVersion >= currentVersion)
                return;
            foreach (IGameDataMigrator migrator in _migrators)
            {
                if (dataVersion < migrator.Version && migrator.Version <= currentVersion)
                    migrator.Migrate(loadedData);
            }
        }

        private void TryLoadData(IDataSaveLoader saveLoader)
        {
            try
            {
                if (_loadedData.TryGetValue(saveLoader.DataId, out JToken serializedData))
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

        private void CollectSaveData()
        {
            _collectedData ??= new Dictionary<string, JToken>(_saveLoaders.Count);
            _collectedData.Clear();
            foreach (IDataSaveLoader saveLoader in _saveLoaders.Values)
            {
                if (!_collectedData.TryAdd(saveLoader.DataId, saveLoader.GetSerializedData()))
                    throw new InvalidOperationException(
                        $"Failed to save game data due to the duplicate id \"{saveLoader.DataId}\"");
            }
        }

        private void SaveGameData(Dictionary<string, JToken> data, int version)
        {
            _gameData ??= new GameData();
            GameDataEntry[] entries = _gameData.Entries?.Length == data.Count
                ? _gameData.Entries
                : new GameDataEntry[data.Count];
            int i = 0;
            foreach (KeyValuePair<string, JToken> pair in data)
            {
                GameDataEntry entry = entries[i];
                entry.Id = pair.Key;
                entry.Payload = pair.Value;
                entries[i] = entry;
                i++;
            }

            _gameData.StructureVersion = CurrentGameDataStructureVersion;
            _gameData.Version = version;
            _gameData.Entries = entries;
            _saveLoader.Save(_arguments.GameDataId, _gameData);
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

        private GameData CreateDefaultGameData() =>
            new GameData { StructureVersion = CurrentGameDataStructureVersion, Version = currentVersion, Entries = Array.Empty<GameDataEntry>() };

        public class Arguments
        {
            public string GameDataId;
            public int MinSupportedVersion;
            public int CurrentVersion;
            public IReadOnlyList<IGameDataMigrator> Migrators;
            public IReadOnlyList<IDataSaveLoader> SaveLoaders;
        }
    }
}
