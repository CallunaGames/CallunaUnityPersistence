using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class GameDataSampleInstaller : MonoInstaller
    {
        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>().ToNew<PersistentDataPathSaveLoader>()
                .WithArgument(new PersistentDataPathSaveLoader.Arguments() { FileName = "GameDataTest" }).AsSingle();
            
            binder.BindToNewSelf<TextFileReadWriter>()
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>().AsSingle();
        }
    }
}