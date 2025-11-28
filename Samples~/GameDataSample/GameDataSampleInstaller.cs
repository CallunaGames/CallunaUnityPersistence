using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class GameDataSampleInstaller : MonoInstaller
    {
        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>().ToNew<PlayerPrefsSaveLoader>().AsSingle();
            
            binder.BindToNewSelf<JsonSerializer>().AsSingle();
        }
    }
}
