using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data1
    {
        public static string Id => "Data1";
        public string Value;
        public Bar Bar;

        public override string ToString()
        {
            return $"Data1: {Value}, Bar: {Bar}";
        }
    }
}
