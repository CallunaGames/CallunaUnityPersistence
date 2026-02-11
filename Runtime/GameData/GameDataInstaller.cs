using System.Collections.Generic;
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
        [SerializeField] private int _lastSupportedVersion = 0;
        [SerializeField] private bool _loadDataOnInit = true;
        [SerializeField] private bool _saveDataOnClean = true;
        
        public override void InstallBindings(Binder binder)
        {
            GameDataPersistence.Arguments arguments = new GameDataPersistence.Arguments()
            {
                Migrators = _migrators,
                GameDataId = _gameDataId,
                SaveLoaders = _saveLoaders,
                LastSupportedVersion = _lastSupportedVersion,
                CurrentVersion = _currentVersion
            };
            
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
