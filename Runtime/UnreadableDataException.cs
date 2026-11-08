using System;

namespace Calluna.Persistence
{
    /// <summary>
    /// Thrown by a <see cref="SaveLoader"/> when the stored data itself is broken - an empty or cut-off
    /// file, invalid JSON, a corrupt database - as opposed to a failure that may pass (a locked file,
    /// denied access). Only this makes <see cref="GameDataPersistence"/> keep the data aside and reset
    /// it with Reset Unreadable Data. The original exception, if any, is the inner exception.
    /// </summary>
    public class UnreadableDataException : Exception
    {
        public UnreadableDataException(string message) : base(message)
        {
        }

        public UnreadableDataException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
