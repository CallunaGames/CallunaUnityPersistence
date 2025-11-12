using System.Collections.Generic;
using Calluna.DI;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class PlayerPrefsInstaller : MonoInstaller, Injectable
    {
        private Resolver _resolver;

        public void Inject(Resolver resolver)
        {
            _resolver = resolver;
        }

        public override void InstallBindings(Binder binder)
        {
            binder.BindToNewSelf<VersionedDataSaveLoader>().AsSingle();

            binder.Bind<SaveLoader>().ToNew<PlayerPrefsSaveLoader>().AsSingle();
            
            binder.BindToNewSelf<JsonSerializer>().AsSingle();

            binder.Bind<IEnumerable<DataMigrator>>()
                .To<List<DataMigrator>>()
                .FromMethod(CreateDataMigrators)
                .AsSingle();

            binder.BindToNewSelf<DataMigrationSteps.DataV0ToV1>();
            binder.BindToNewSelf<DataMigrationSteps.DataV1ToV2>();
        }

        private List<DataMigrator> CreateDataMigrators()
        {
            List<DataMigrationStep> steps = new List<DataMigrationStep>()
            {
                _resolver.Resolve<DataMigrationSteps.DataV0ToV1>(),
                _resolver.Resolve<DataMigrationSteps.DataV1ToV2>(),
            };

            DataMigrator dataMigrator = new DataMigrator(DataV2.Id, steps);
            return new List<DataMigrator>() { dataMigrator };
        }
    }
}