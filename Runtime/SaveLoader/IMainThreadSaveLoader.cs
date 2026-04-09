namespace Calluna.Persistence
{
    /// <summary>
    /// Marker interface for <see cref="SaveLoader"/> implementations whose write operations
    /// must execute on Unity's main thread (e.g. <see cref="PlayerPrefsSaveLoader"/>).
    /// <see cref="GameDataPersistence.SaveAsync"/> detects this interface and falls back to
    /// a synchronous write when the backend requires it.
    /// </summary>
    internal interface IMainThreadSaveLoader { }
}
