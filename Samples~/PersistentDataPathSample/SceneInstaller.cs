using System.Globalization;
using Calluna.DI;
using Newtonsoft.Json;

namespace Calluna.Persistence.Samples.PersistentDataPath
{
    public class SceneInstaller : MonoInstaller
    {
        public override void InstallBindings(Binder binder)
        {
            binder.BindInstance(new JsonSerializerSettings
                { Formatting = Formatting.Indented, Culture = CultureInfo.CurrentCulture });
        }
    }
}