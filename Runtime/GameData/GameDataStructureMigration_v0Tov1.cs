using System;

namespace Calluna.Persistence
{
    /// <summary>
    /// Built-in GameData structure migration from version 0 to version 1.
    /// Handles the rename of GameData.Entries (stored as "Data" in v0) and
    /// GameDataEntry.Payload (stored as "Data" in v0).
    /// This step is registered automatically by <see cref="GameDataPersistence"/>.
    /// </summary>
    internal class GameDataStructureMigration_v0Tov1 : GameDataStructureMigrationStep<GameData_v0, GameData>
    {
        public GameDataStructureMigration_v0Tov1(JsonSerializer serializer) : base(serializer) { }

        public override int TargetVersion => 1;

        protected override GameData Migrate(GameData_v0 old)
        {
            GameDataEntry[] entries;
            if (old.Data == null || old.Data.Length == 0)
            {
                entries = Array.Empty<GameDataEntry>();
            }
            else
            {
                entries = new GameDataEntry[old.Data.Length];
                for (int i = 0; i < old.Data.Length; i++)
                    entries[i] = new GameDataEntry { Id = old.Data[i].Id, Payload = old.Data[i].Data };
            }
            return new GameData { Version = old.Version, Entries = entries };
        }
    }
}
