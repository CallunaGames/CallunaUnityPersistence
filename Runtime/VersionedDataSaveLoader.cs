using System;
using System.Collections.Generic;
using System.Linq;
using Calluna.DI;

namespace Calluna.Persistence
{
    public class VersionedDataSaveLoader : Injectable
    {
        private Dictionary<Type, DataMigrator> _dataMigrators;
        private SaveLoader _saveLoader;
        private JsonSerializer _serializer;

        public void Inject(Resolver resolver)
        {
            _dataMigrators = CreateMigratorDictionary(resolver.ResolveOptional<IEnumerable<DataMigrator>>());
            _saveLoader = resolver.Resolve<SaveLoader>();
            _serializer = resolver.Resolve<JsonSerializer>();
        }

        public T Load<T>(string id, T defaultValue = default)
        {
            VersionedSaveData saveData = _saveLoader.Load<VersionedSaveData>(id);

            if (saveData == null)
                return defaultValue;

            if (!_dataMigrators.TryGetValue(typeof(T), out DataMigrator migrator))
            {
                throw new ArgumentException($"There is missing a data migrator for '{typeof(T)}'");
            }

            string data = migrator.Migrate(saveData);
            return _serializer.Deserialize<T>(data);
        }

        public void Save<T>(string id, T value, int version)
        {
            string data = _serializer.Serialize(value);
            VersionedSaveData versionedData = new VersionedSaveData() { Data = data, Version = version };
            _saveLoader.Save(id, versionedData);
        }

        private Dictionary<Type, DataMigrator> CreateMigratorDictionary(IEnumerable<DataMigrator> migrators)
        {
            return migrators == null ? new Dictionary<Type, DataMigrator>() : migrators.ToDictionary(m => m.DataType);
        }
    }
}