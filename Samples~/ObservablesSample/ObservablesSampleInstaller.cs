using System.Collections.Generic;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.Samples.Observables
{
    /// <summary>
    /// Sets up a minimal GameDataPersistence scene to demonstrate subscribing to
    /// LoadingFailed, DataWasReset, and SaveLoader.OnClear.
    /// Attach this and GameDataInstaller to a MonoContext in the scene.
    /// </summary>
    public class ObservablesSampleInstaller : MonoInstaller
    {
        [SerializeField] private string _fileName = "ObservablesSampleData.txt";

        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>()
                .ToNew<PersistentDataPathSaveLoader>()
                .WithArgument(new PersistentDataPathSaveLoader.Arguments { FileName = _fileName })
                .AsSingle();

            binder.BindToNewSelf<TextFileReadWriter>()
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>()
                .AsSingle();
            
            
        }
    }
}
