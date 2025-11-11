using Calluna.DI;

namespace Calluna.Persistence
{
    public abstract class DataMigrationStep
    {
        public abstract int TargetVersion { get; }
        public abstract string Migrate(string data);
    }

    public abstract class DataMigrationStep<TFrom, TTo> : DataMigrationStep, Injectable
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
