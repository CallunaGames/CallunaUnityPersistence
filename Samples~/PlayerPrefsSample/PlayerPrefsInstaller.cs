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

            binder.Bind<IEnumerable<VersionedDataMigrator>>()
                .To<List<VersionedDataMigrator>>()
                .FromMethod(CreateDataMigrators)
                .AsSingle();

            binder.BindToNewSelf<DataMigrationSteps.DataV0ToV1>();
            binder.BindToNewSelf<DataMigrationSteps.DataV1ToV2>();
        }

        private List<VersionedDataMigrator> CreateDataMigrators()
        {
            List<VersionedDataMigrationStep> steps = new List<VersionedDataMigrationStep>()
            {
                _resolver.Resolve<DataMigrationSteps.DataV0ToV1>(),
                _resolver.Resolve<DataMigrationSteps.DataV1ToV2>(),
            };

            VersionedDataMigrator dataMigrator = new VersionedDataMigrator<DataV2>(steps);
            return new List<VersionedDataMigrator>() { dataMigrator };
        }
    }
}
