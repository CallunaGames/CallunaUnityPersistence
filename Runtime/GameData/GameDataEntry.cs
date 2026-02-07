using System;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    [Serializable]
    public struct GameDataEntry
    {
        public string Id;
        public JToken Data;
    }
}