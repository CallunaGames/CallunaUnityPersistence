using System.Globalization;
using Calluna.DI;
using Newtonsoft.Json;
using UnityEngine;

namespace Calluna.Persistence.Samples.PersistentDataPath
{
    public class SceneInstaller : MonoInstaller
    {
        [SerializeField] private string _fileName = "MyData.txt";

        public override void InstallBindings(Binder binder)
        {
            binder.Bind<SaveLoader>()
                .ToNew<PersistentDataPathSaveLoader>()
                .WithArgument(new PersistentDataPathSaveLoader.Arguments { FileName = _fileName })
                .AsSingle();

            binder.BindToNewSelf<TextFileReadWriter>()
                .AsSingle();

            binder.BindToNewSelf<JsonSerializer>()
                .WithArgument(new JsonSerializerSettings
                    { Formatting = Formatting.Indented, Culture = CultureInfo.CurrentCulture })
                .AsSingle();
        }
    }
}