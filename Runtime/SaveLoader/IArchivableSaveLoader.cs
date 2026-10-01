namespace Calluna.Persistence
{
    /// <summary>
    /// Optional internal interface for <see cref="SaveLoader"/> implementations that can keep their
    /// current data aside instead of overwriting it - used by
    /// <see cref="GameDataPersistence.RestoreBackup"/>, so the replaced (possibly broken) save is still
    /// available for support.
    /// </summary>
    internal interface IArchivableSaveLoader
    {
        /// <summary>
        /// Moves the stored data aside by appending <paramref name="suffix"/> to its file name(s).
        /// Afterwards the loader is empty and starts a new store on its next write.
        /// </summary>
        void Archive(string suffix);
    }
}
