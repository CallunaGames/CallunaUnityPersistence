using Newtonsoft.Json;

namespace Calluna.Persistence
{
    internal class GameData
    {
        [JsonProperty] internal int StructureVersion;
        [JsonProperty] internal int Version;
        [JsonProperty] internal GameDataEntry[] Entries;
    }
}
