using Calluna.DI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    /// <summary>
    /// Serializes to and from JSON strings and <see cref="JToken"/>s with one configuration: a bound
    /// <see cref="JsonSerializerSettings"/> (e.g. with custom converters) applies to both. Without
    /// bound settings: invariant culture, null values omitted, no indentation. A bound
    /// <see cref="Newtonsoft.Json.JsonSerializer"/> replaces the one used for tokens.
    /// </summary>
    public class JsonSerializer : Injectable
    {
        private JsonSerializerSettings _settings;
        private Newtonsoft.Json.JsonSerializer _jsonSerializer;

        public void Inject(Resolver resolver)
        {
            _settings = resolver.ResolveOptional<JsonSerializerSettings>() ?? CreateDefaultSettings();
            _jsonSerializer = resolver.ResolveOptional<Newtonsoft.Json.JsonSerializer>()
                              ?? Newtonsoft.Json.JsonSerializer.Create(_settings);
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

        // The former defaults of the token serializer - now used for strings as well.
        private static JsonSerializerSettings CreateDefaultSettings()
        {
            return new JsonSerializerSettings
            {
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };
        }
    }
}