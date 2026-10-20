namespace Calluna.Persistence
{
    public enum SaveOutcome
    {
        /// <summary>The data was written to storage.</summary>
        Saved,
        /// <summary>Nothing was written on purpose - loading failed, or a restored backup must not be overwritten.</summary>
        Skipped,
        /// <summary>Serializing or writing threw; the stored data is the one of the last successful save.</summary>
        Failed
    }
}
