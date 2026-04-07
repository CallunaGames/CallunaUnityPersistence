using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Scene-level MonoInstaller that binds <see cref="PlayerPrefsSaveLoader"/> as the <see cref="SaveLoader"/>
    /// and wires a default <see cref="JsonSerializer"/>.
    /// Add this to your MonoContext when you want PlayerPrefs-backed persistence.
    /// </summary>
    public class PlayerPrefsSaveLoaderInstaller : MonoInstaller
    {
        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>()
                .ToNew<PlayerPrefsSaveLoader>()
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>()
                .AsSingle();
        }
    }
}
