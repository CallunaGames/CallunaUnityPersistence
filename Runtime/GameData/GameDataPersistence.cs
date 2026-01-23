using System;
using System.Collections.Generic;
using System.Linq;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public class GameDataPersistence : Injectable, Initializable
    {
        public ReadonlyObservable<bool> LoadingFailed => _loadFailed;
        
        private Dictionary<string, DataSaveLoader> _saveLoaders = new Dictionary<string, DataSaveLoader>();
        private Dictionary<string, string> _loadedData = new Dictionary<string, string>();

        private Arguments _arguments;
        private SaveLoader _saveLoader;
        private IOrderedEnumerable<GameDataMigrator> _migrators;
        private int _currentVersion = 0;
        private readonly Observable<bool> _loadFailed = false;

        private Dictionary<string, string> _collectedData;
        private GameData _gameData;
        
        void Initializable.Initialize()
        {
            _loadFailed.Value = false;
        }
        
        void Injectable.Inject(Resolver resolver)
        {
            _arguments = resolver.Resolve<Arguments>();
            _saveLoader = resolver.Resolve<SaveLoader>();
        }

        /// <summary>
        /// Loads the saved GameData. Uses the default value of each data if no GameData is found.
        /// </summary>
        public void Load()
        {
            try
            {
                InitMigrators();
            
                _gameData = _saveLoader.Load(_arguments.GameDataId, CreateDefaultGameData());
                _loadedData = _gameData.Data.ToDictionary(d => d.Id, d => d.Data);
                MigrateData(_loadedData, _gameData.Version);

                _saveLoaders = _arguments.SaveLoaders.ToDictionary(s => s.DataId);
                LoadDataSaveLoaders();
            }
            catch (Exception e)
            {
                _loadFailed.Value = true;
                Debug.LogError("Failed to load the game data");
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
                SaveGameData(_collectedData, _currentVersion);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to save the game data");
                Debug.LogException(e);
            }
        }
        
        /// <summary>
        /// Saves game data with custom data dictionary and version. Use with caution!
        /// </summary>
        /// <param name="data">The serialized key value pairs (Data Id, Serialized Data)</param>
        /// <param name="version">The custom version of this save data</param>
        public void OverrideSave(Dictionary<string, string> data, int version) => SaveGameData(data, version);

        private void InitMigrators()
        {
            if (_arguments.Migrators == null || _arguments.Migrators.Count == 0)
            {
                _migrators = new List<GameDataMigrator>().OrderBy(m => m.Version);
                return;
            }

            ValidateVersions(_arguments.Migrators);
            _migrators = _arguments.Migrators.OrderBy(m => m.Version);
            _currentVersion = _migrators.Last().Version;
        }

        private void MigrateData(Dictionary<string, string> loadedData, int dataVersion)
        {
            if (dataVersion == _currentVersion)
                return;
            foreach (GameDataMigrator migrator in _migrators)
            {
                if(dataVersion < migrator.Version)
                    migrator.Migrate(loadedData);
            }
        }

        private void LoadDataSaveLoaders()
        {
            foreach (DataSaveLoader saveLoader in _saveLoaders.Values)
            {
                TryLoadData(saveLoader);
            }
        }

        private void TryLoadData(DataSaveLoader saveLoader)
        {
            if (_loadedData.TryGetValue(saveLoader.DataId, out string serializedData))
                saveLoader.Load(serializedData);
            else
                saveLoader.LoadDefault();
        }

        private void CollectSaveData()
        {
            _collectedData ??= new Dictionary<string, string>(_saveLoaders.Count);
            _collectedData.Clear();
            foreach (DataSaveLoader saveLoader in _saveLoaders.Values)
            {
                if (!_collectedData.TryAdd(saveLoader.DataId, saveLoader.GetSerializedData()))
                    throw new InvalidOperationException(
                        $"Failed to save game data do to the duplicate id \"{saveLoader.DataId}\"");
            }
        }

        private void SaveGameData(Dictionary<string, string> data, int version)
        {
            _gameData ??= new GameData();
            GameDataEntry[] entries = _gameData.Data?.Length == _collectedData.Count ? _gameData.Data : new GameDataEntry[_collectedData.Count];
            int i = 0;
            foreach (KeyValuePair<string, string> pair in data)
            {
                GameDataEntry entry = entries[i];
                entry.Id = pair.Key;
                entry.Data = pair.Value;
                entries[i] = entry;
            }
            _gameData.Version = version;
            _gameData.Data = entries;
            _saveLoader.Save(_arguments.GameDataId, _gameData);
        }

        private void ValidateVersions(IReadOnlyList<GameDataMigrator> migrators)
        {
            HashSet<int> versions = new HashSet<int>();
            foreach (GameDataMigrator migrator in migrators)
            {
                if (migrator.Version <= 0)
                    throw new ArgumentException("Game data migrator version is invalid. It must be greater than 0.");
                if (!versions.Add(migrator.Version))
                    throw new ArgumentException("Game data migrator has already been registered with version " +
                                                migrator.Version);
            }
        }

        private GameData CreateDefaultGameData()
        {
            return new GameData() { Version = _currentVersion, Data = Array.Empty<GameDataEntry>() };
        }

        public class Arguments
        {
            public string GameDataId;
            public IReadOnlyList<GameDataMigrator> Migrators;
            public IReadOnlyList<DataSaveLoader> SaveLoaders;
        }
    }
}