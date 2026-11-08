using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Scene-level MonoInstaller that binds <see cref="PersistentDataPathSaveLoader"/> as the <see cref="SaveLoader"/>
    /// and wires all required internal dependencies including <see cref="JsonSerializer"/>.
    /// Add this to your MonoContext when you want file-based persistence.
    /// Configure the file name via the <see cref="FileName"/> Inspector field.
    /// </summary>
    public class PersistentDataPathSaveLoaderInstaller : MonoInstaller
    {
        [SerializeField] private string _fileName = "SaveData.txt";

        [Tooltip("Writes indented, human-readable JSON - e.g. for a settings file players may open. Off: one line.")]
        [SerializeField] private bool _indented;

        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>()
                .ToNew<PersistentDataPathSaveLoader>()
                .WithArgument(new PersistentDataPathSaveLoader.Arguments { FileName = _fileName, Indented = _indented })
                .AsSingle();

            binder.BindToNewSelf<TextFileReadWriter>()
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>()
                .AsSingle();
        }
    }
}
