using System;
using System.Collections.Generic;
using System.Linq;

namespace Calluna.Persistence
{
    public class DataMigrator<T> : DataMigrator
    {
        public DataMigrator(IEnumerable<DataMigrationStep> steps = null) : base(steps) { }
        
        public override Type DataType => typeof(T);
    }
    
    public abstract class DataMigrator
    {
        public abstract Type DataType { get; }

        private Dictionary<int, DataMigrationStep> _steps;
        private int _currentVersion;

        public DataMigrator(IEnumerable<DataMigrationStep> steps = null)
        {
            bool hasSteps = steps != null;
            _steps = hasSteps ? steps.ToDictionary(s => s.TargetVersion) : new Dictionary<int, DataMigrationStep>();
            _currentVersion = hasSteps ? steps.Min(s => s.TargetVersion) : 0;
        }

        public string Migrate(VersionedSaveData versionedSaveData)
        {
            int version = versionedSaveData.Version;
            string data = versionedSaveData.Data;
            for (int i = version; i < _currentVersion; i++)
            {
                int migratorVersion = i + 1;
                data = _steps[migratorVersion].Migrate(data);
            }

            return data;
        }
    }
}