using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    internal class GameData_v0
    {
        public int Version;
        public GameDataEntry_v0[] Data;
    }

    internal struct GameDataEntry_v0
    {
        public string Id;
        public JToken Data;
    }
}
