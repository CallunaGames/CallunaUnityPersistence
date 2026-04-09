namespace Calluna.Persistence
{
    /// <summary>
    /// Optional internal interface for <see cref="SaveLoader"/> implementations that support
    /// grouping multiple <see cref="SaveLoader.Save{T}"/> calls into a single atomic write.
    /// <para>
    /// <see cref="GameDataPersistence"/> checks for this interface at save time and wraps its
    /// write loop in a batch when supported, reducing per-write overhead significantly (e.g.
    /// one SQLite transaction instead of one per key).
    /// </para>
    /// This interface is intentionally not part of <see cref="SaveLoader"/> — callers that use
    /// <see cref="SaveLoader"/> directly are unaffected.
    /// </summary>
    internal interface IBatchableSaveLoader
    {
        /// <summary>Opens a write batch (e.g. begins a database transaction).</summary>
        void BeginBatch();

        /// <summary>Commits all writes since <see cref="BeginBatch"/>.</summary>
        void CommitBatch();

        /// <summary>Rolls back all writes since <see cref="BeginBatch"/>.</summary>
        void RollbackBatch();
    }
}
