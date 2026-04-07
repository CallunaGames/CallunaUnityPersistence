using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    internal struct GameDataEntry
    {
        [JsonProperty] internal string Id;
        [JsonProperty("Data")] internal JToken Payload;
    }
}
