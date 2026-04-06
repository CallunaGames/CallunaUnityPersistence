namespace Calluna.Persistence
{
    internal abstract class GameDataStructureMigrationStep
    {
        public abstract int TargetVersion { get; }
        public abstract string Migrate(string jsonData);
    }

    internal abstract class GameDataStructureMigrationStep<TFrom, TTo> : GameDataStructureMigrationStep
    {
        private readonly JsonSerializer _serializer;

        protected GameDataStructureMigrationStep(JsonSerializer serializer)
        {
            _serializer = serializer;
        }

        public override string Migrate(string jsonData)
        {
            TFrom from = _serializer.Deserialize<TFrom>(jsonData);
            TTo result = Migrate(from);
            return _serializer.Serialize(result);
        }

        protected abstract TTo Migrate(TFrom data);
    }
}
