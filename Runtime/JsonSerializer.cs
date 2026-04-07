using Calluna.DI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    public class JsonSerializer : Injectable
    {
        private JsonSerializerSettings _settings;
        private Newtonsoft.Json.JsonSerializer _jsonSerializer;

        public void Inject(Resolver resolver)
        {
            _settings = resolver.ResolveOptional<JsonSerializerSettings>() ?? new JsonSerializerSettings();
            _jsonSerializer = resolver.ResolveOptional<Newtonsoft.Json.JsonSerializer>() ?? CreateDefaultSerializer();
        }

        public string Serialize<T>(T value) =>
            JsonConvert.SerializeObject(value, _settings);

        public JToken SerializeToToken<T>(T value) =>
            JToken.FromObject(value, _jsonSerializer);

        public T Deserialize<T>(string stringData) => !string.IsNullOrEmpty(stringData) ?
            JsonConvert.DeserializeObject<T>(stringData, _settings) : default;

        public T Deserialize<T>(JToken token)
        {
            if (token is JValue jValue)
                return Deserialize<T>(jValue);
            return token.ToObject<T>(_jsonSerializer);
        }

        private T Deserialize<T>(JValue jValue)
        {
            if (jValue.Type == JTokenType.String && typeof(T) != typeof(string))
                return Deserialize<T>(jValue.Value<string>());
            return jValue.ToObject<T>(_jsonSerializer);
        }

        private Newtonsoft.Json.JsonSerializer CreateDefaultSerializer()
        {
            return new Newtonsoft.Json.JsonSerializer()
            {
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.None
            };
        }
    }
}