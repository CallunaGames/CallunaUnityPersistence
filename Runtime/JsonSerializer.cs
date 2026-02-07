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

        public string Serialize<T>(T value)
        {
            string data = JsonConvert.SerializeObject(value, _settings);
            return data;
        }

        public JToken SerializeToToken<T>(T value)
        {
            JToken data = JToken.FromObject(value, _jsonSerializer);
            return data;
        }

        public T Deserialize<T>(string stringData)
        {
            T data = JsonConvert.DeserializeObject<T>(stringData, _settings);
            return data;
        }

        public T Deserialize<T>(JToken token)
        {
            if (token is JValue jValue)
                return Deserialize<T>(jValue);
            if (token is JObject jObject)
                return jObject.ToObject<T>(_jsonSerializer);
            if (token is JArray jArray)
                return jArray.ToObject<T>(_jsonSerializer);
            return token.ToObject<T>(_jsonSerializer);
        }

        private T Deserialize<T>(JValue jValue)
        {
            if (jValue.Type == JTokenType.String)
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