using System;

namespace Calluna.Persistence
{
    /// <summary>
    /// A backup of the game data, created by <see cref="GameDataPersistence.Load"/> after a successful
    /// load. List them with <see cref="GameDataPersistence.GetBackups"/> and restore one with
    /// <see cref="GameDataPersistence.RestoreBackup"/>.
    /// </summary>
    public sealed class GameDataBackup
    {
        /// <summary>The save data version of the backed up data.</summary>
        public int SaveVersion { get; }

        /// <summary>When the backup was created (UTC).</summary>
        public DateTime CreatedUtc { get; }

        /// <summary>The <c>Application.version</c> of the game that created the backup.</summary>
        public string GameVersion { get; }

        internal string FilePath { get; }

        internal GameDataBackup(int saveVersion, DateTime createdUtc, string gameVersion, string filePath)
        {
            SaveVersion = saveVersion;
            CreatedUtc = createdUtc;
            GameVersion = gameVersion;
            FilePath = filePath;
        }

        public override string ToString() => $"Backup of save version {SaveVersion} from {CreatedUtc:u} (game {GameVersion})";
    }
}
