using System;
using System.IO;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Scene-level MonoInstaller that binds <see cref="SqliteSaveLoader"/> as the <see cref="SaveLoader"/>
    /// and wires all required internal dependencies including <see cref="JsonSerializer"/>.
    /// Add this to your MonoContext when you want SQLite-backed persistence.
    /// Configure the database file name via the <see cref="_fileName"/> Inspector field.
    /// <para><b>Platform note:</b> WebGL is not supported.</para>
    /// </summary>
    public class SqliteSaveLoaderInstaller : MonoInstaller
    {
        [SerializeField] private string _fileName = "SaveData.db";

        [Tooltip("Opens the connection with SQLITE_OPEN_FULLMUTEX so it is safe to use from " +
                 "multiple threads. Enable this when using GameDataPersistence.SaveAsync().")]
        [SerializeField] private bool _fullMutex = false;

        [Tooltip("Sets PRAGMA synchronous=OFF, removing WAL frame syncing. Reduces per-transaction " +
                 "overhead significantly at the cost of potential data loss on OS crash or power failure. " +
                 "Safe on normal application exit.")]
        [SerializeField] private bool _synchronousOff = false;

        private static readonly string[] ValidExtensions = { ".db", ".sqlite", ".sqlite3", ".db3" };

        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>()
                .ToNew<SqliteSaveLoader>()
                .WithArgument(new SqliteSaveLoader.Arguments
                {
                    FileName = ResolveFileName(_fileName),
                    FullMutex = _fullMutex,
                    SynchronousOff = _synchronousOff,
                })
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>()
                .AsSingle();
        }

        private static string ResolveFileName(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                return fileName + ".db";
            if (Array.IndexOf(ValidExtensions, extension.ToLowerInvariant()) < 0)
                throw new ArgumentException(
                    $"SqliteSaveLoaderInstaller: invalid file extension '{extension}' for '{fileName}'. " +
                    $"Expected one of: {string.Join(", ", ValidExtensions)}");
            return fileName;
        }
    }
}
