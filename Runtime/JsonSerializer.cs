using Calluna.DI;
using Newtonsoft.Json;

namespace Calluna.Persistence
{
    public class JsonSerializer : Injectable
    {
        private JsonSerializerSettings _settings;
        
        public void Inject(Resolver resolver)
        {
            _settings = resolver.ResolveOptional<JsonSerializerSettings>() ?? new JsonSerializerSettings();
        }

        public string Serialize<T>(T value)
        {
            string data = JsonConvert.SerializeObject(value, _settings);
            return data;
        }

        public T Deserialize<T>(string stringData)
        {
            T data = JsonConvert.DeserializeObject<T>(stringData, _settings);
            return data;
        }
    }
}