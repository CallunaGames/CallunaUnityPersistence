using System;
using System.Collections.Generic;

namespace Calluna.Persistence
{
    /// <summary>
    /// Migrates a single typed value across versions. Used exclusively with <see cref="VersionedDataSaveLoader"/>.
    /// This is independent of <see cref="GameDataMigrator"/>, which operates on the composite GameData dictionary.
    /// </summary>
    public class VersionedDataMigrator<T> : VersionedDataMigrator
    {
        public VersionedDataMigrator(IEnumerable<VersionedDataMigrationStep> steps = null) : base(steps) { }

        internal override Type DataType => typeof(T);
    }

    public abstract class VersionedDataMigrator
    {
        internal abstract Type DataType { get; }

        private Dictionary<int, VersionedDataMigrationStep> _steps;
        private int _currentVersion;

        public VersionedDataMigrator(IEnumerable<VersionedDataMigrationStep> steps = null)
        {
            if (steps == null)
            {
                _steps = new Dictionary<int, VersionedDataMigrationStep>();
                _currentVersion = 0;
                return;
            }

            _steps = new Dictionary<int, VersionedDataMigrationStep>();
            _currentVersion = 0;
            foreach (VersionedDataMigrationStep step in steps)
            {
                _steps[step.TargetVersion] = step;
                if (step.TargetVersion > _currentVersion)
                    _currentVersion = step.TargetVersion;
            }
        }

        internal string Migrate(VersionedSaveData versionedSaveData)
        {
            int version = versionedSaveData.Version;
            string data = versionedSaveData.Data;
            for (int i = version; i < _currentVersion; i++)
            {
                int migratorVersion = i + 1;
                if (!_steps.TryGetValue(migratorVersion, out VersionedDataMigrationStep step))
                    throw new InvalidOperationException(
                        $"No migration step registered for version {migratorVersion} on type '{DataType.Name}'.");
                data = step.Migrate(data);
            }

            return data;
        }
    }
}
