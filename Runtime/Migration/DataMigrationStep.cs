using Calluna.DI;

namespace Calluna.Persistence
{
    /// <summary>
    /// A single migration step between two consecutive versions of a typed value.
    /// Used with <see cref="VersionedDataMigrator{T}"/> and <see cref="VersionedDataSaveLoader"/>.
    /// This is independent of <see cref="GameDataMigrator"/>, which operates on the composite GameData dictionary.
    /// </summary>
    public abstract class VersionedDataMigrationStep
    {
        public abstract int TargetVersion { get; }
        public abstract string Migrate(string data);
    }

    public abstract class VersionedDataMigrationStep<TFrom, TTo> : VersionedDataMigrationStep, Injectable
    {
        private JsonSerializer _serializer;

        public virtual void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
        }

        public override string Migrate(string data)
        {
            TFrom from = _serializer.Deserialize<TFrom>(data);
            TTo result = Migrate(from);
            return _serializer.Serialize(result);
        }

        protected abstract TTo Migrate(TFrom data);
    }
}
