using System;
using System.Collections.Generic;

namespace Calluna.Persistence
{
    /// <summary>What made <see cref="GameDataPersistence.Load"/> fail.</summary>
    public enum GameDataLoadFailure
    {
        /// <summary>Loading succeeded (or hasn't run yet).</summary>
        None,

        /// <summary>
        /// The setup is invalid, e.g. two DataSaveLoaders with the same id or an invalid migrator
        /// version - a bug in the game, not in the save.
        /// </summary>
        Configuration,

        /// <summary>The stored data couldn't be read (or the migrated data couldn't be written).</summary>
        Storage,

        /// <summary>A <see cref="IGameDataMigrator"/> threw.</summary>
        Migration,

        /// <summary>One or more DataSaveLoaders threw - see <see cref="GameDataLoadResult.FailedDataIds"/>.</summary>
        DataSaveLoader,

        /// <summary>
        /// The stored data has a newer version than the current one - a newer game version saved it.
        /// Loading it would lose what this version doesn't know.
        /// </summary>
        NewerVersion,
    }

    /// <summary>
    /// The outcome of <see cref="GameDataPersistence.Load"/>. A failed load leaves the stored data
    /// untouched, so it can still be restored from a backup or examined.
    /// </summary>
    public sealed class GameDataLoadResult
    {
        internal static readonly GameDataLoadResult NotLoaded =
            new GameDataLoadResult(GameDataLoadFailure.None, null, Array.Empty<string>(), Array.Empty<Exception>());

        /// <summary>True unless loading failed. Also true before the first load.</summary>
        public bool Succeeded => Failure == GameDataLoadFailure.None;

        public GameDataLoadFailure Failure { get; }

        /// <summary>
        /// The failure the load recovered from by keeping the stored data aside and resetting it to
        /// defaults (Reset Unreadable Data, see <see cref="GameDataInstaller"/>); <see cref="GameDataLoadFailure.None"/>
        /// otherwise. <see cref="StoredVersion"/>, <see cref="FailedDataIds"/> and <see cref="Exceptions"/>
        /// then describe that failure.
        /// </summary>
        public GameDataLoadFailure RecoveredFrom { get; }

        /// <summary>The version the stored data had, or null if there was none (or it couldn't be read).</summary>
        public int? StoredVersion { get; }

        /// <summary>The ids of the DataSaveLoaders that threw while loading their data.</summary>
        public IReadOnlyList<string> FailedDataIds { get; }

        /// <summary>
        /// The exceptions that made loading fail (or that it recovered from) - one per failed
        /// DataSaveLoader, otherwise one.
        /// </summary>
        public IReadOnlyList<Exception> Exceptions { get; }

        internal GameDataLoadResult(GameDataLoadFailure failure, int? storedVersion,
            IReadOnlyList<string> failedDataIds, IReadOnlyList<Exception> exceptions,
            GameDataLoadFailure recoveredFrom = GameDataLoadFailure.None)
        {
            Failure = failure;
            StoredVersion = storedVersion;
            FailedDataIds = failedDataIds;
            Exceptions = exceptions;
            RecoveredFrom = recoveredFrom;
        }

        internal static GameDataLoadResult Success(int? storedVersion) =>
            new GameDataLoadResult(GameDataLoadFailure.None, storedVersion, Array.Empty<string>(), Array.Empty<Exception>());

        internal static GameDataLoadResult Failed(GameDataLoadFailure failure, int? storedVersion, Exception exception) =>
            new GameDataLoadResult(failure, storedVersion, Array.Empty<string>(), new[] { exception });

        internal static GameDataLoadResult Recovered(GameDataLoadResult failed) =>
            new GameDataLoadResult(GameDataLoadFailure.None, failed.StoredVersion, failed.FailedDataIds,
                failed.Exceptions, failed.Failure);

        public override string ToString()
        {
            if (Succeeded && RecoveredFrom == GameDataLoadFailure.None)
                return "Loaded";
            string details = $"stored version {StoredVersion?.ToString() ?? "none"}" +
                             (FailedDataIds.Count > 0 ? $", failed data: {string.Join(", ", FailedDataIds)}" : "");
            return Succeeded
                ? $"Reset to defaults after a {RecoveredFrom} failure ({details})"
                : $"{Failure} failure ({details})";
        }
    }
}
