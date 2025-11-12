using UnityEngine;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class DataV2 : MigratableData
    {
        public static string Id => "Data";
        public string Value;
        public string DataId => Id;

        public override string ToString()
        {
            return Value;
        }
    }
}
