using System;

namespace Calluna.Persistence
{
    /// <summary>
    /// The outcome of <see cref="GameDataPersistence.Save"/> and <see cref="GameDataPersistence.SaveAsync"/>.
    /// </summary>
    public readonly struct SaveResult
    {
        public SaveOutcome Outcome { get; }
        /// <summary>Why it failed; null unless <see cref="Outcome"/> is <see cref="SaveOutcome.Failed"/>.</summary>
        public Exception Exception { get; }
        /// <summary>What was skipped or failed, for logs.</summary>
        public string Reason { get; }

        public bool Succeeded => Outcome == SaveOutcome.Saved;

        private SaveResult(SaveOutcome outcome, Exception exception, string reason)
        {
            Outcome = outcome;
            Exception = exception;
            Reason = reason;
        }

        internal static SaveResult Saved() => new SaveResult(SaveOutcome.Saved, null, null);

        internal static SaveResult Skipped(string reason) => new SaveResult(SaveOutcome.Skipped, null, reason);

        internal static SaveResult Failed(string reason, Exception exception) =>
            new SaveResult(SaveOutcome.Failed, exception, reason);

        public override string ToString() => Outcome switch
        {
            SaveOutcome.Saved => "Saved",
            SaveOutcome.Skipped => $"Skipped: {Reason}",
            _ => $"Failed: {Reason} ({Exception?.GetType().Name})"
        };
    }
}
