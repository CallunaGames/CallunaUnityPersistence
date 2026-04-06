using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    public struct GameDataEntry
    {
        public string Id;
        [JsonProperty("Data")]
        public JToken Payload;
    }
}
