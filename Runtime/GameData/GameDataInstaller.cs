using System.Collections.Generic;
using System.IO;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public class GameDataInstaller : MonoInstaller
    {
        [SerializeField] private List<GameDataMigrator> _migrators = new List<GameDataMigrator>();
        [SerializeField] private List<DataSaveLoader> _saveLoaders = new List<DataSaveLoader>();
        [SerializeField] private string _gameDataId = "__GameData__";
        [SerializeField] private int _currentVersion = 0;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_lastSupportedVersion")]
        private int _minSupportedVersion = 0;
        [SerializeField] private bool _loadDataOnInit = true;
        [SerializeField] private bool _saveDataOnClean = true;

        [Header("Backups")]
        [Tooltip("Backs up the game data after every successful load, so a save that can't be loaded " +
                 "can be restored from a backup (GameDataPersistence.GetBackups / RestoreBackup).")]
        [SerializeField] private bool _createBackups = true;
        [Tooltip("Backups kept per save data version (the newest ones). The newest backup of the game " +
                 "build before the latest one is kept in addition.")]
        [SerializeField, Min(1)] private int _backupsPerVersion = 3;
        [Tooltip("Save data versions to keep backups of: the current one and older ones, e.g. to roll " +
                 "back after an update. Backups of newer versions are never deleted.")]
        [SerializeField, Min(1)] private int _backupVersionsKept = 3;
        [Tooltip("Folder of the backups - relative to Application.persistentDataPath, or absolute. " +
                 "Keep it out of cloud sync if the save folder is synced.")]
        [SerializeField] private string _backupFolder = "Backups";
        
        public override void InstallBindings(Binder binder)
        {
            GameDataPersistence.Arguments arguments = new GameDataPersistence.Arguments()
            {
                Migrators = _migrators.ConvertAll(x => (IGameDataMigrator)x),
                GameDataId = _gameDataId,
                SaveLoaders = _saveLoaders.ConvertAll(x => (IDataSaveLoader)x),
                MinSupportedVersion = _minSupportedVersion,
                CurrentVersion = _currentVersion,
                Backups = _createBackups
                    ? new GameDataBackupStore.Settings
                    {
                        Directory = Path.Combine(Application.persistentDataPath, _backupFolder),
                        BackupsPerVersion = _backupsPerVersion,
                        VersionsKept = _backupVersionsKept,
                    }
                    : null,
            };
            
            binder.BindToNewSelf<GameDataWriter>()
                .WithArgument(new GameDataWriter.Arguments
                {
                    GameDataId = _gameDataId,
                    CurrentVersion = _currentVersion,
                })
                .AsSingle();

            binder.BindToNewSelf<GameDataPersistence>()
                .WithArgument(arguments)
                .AsSingle()
                .NonLazy();

            if (_loadDataOnInit)
            {
                binder.BindComponent<LoadGameDataOnInit>()
                    .FromNewComponentOnNewGameObject("LoadGameDataOnInit", transform)
                    .AsNonResolvable();
            }

            if (_saveDataOnClean)
            {
                binder.BindComponent<SaveGameDataOnClean>()
                    .FromNewComponentOnNewGameObject("SaveGameDataOnClean", transform)
                    .AsNonResolvable();
            }
        }
    }
}
