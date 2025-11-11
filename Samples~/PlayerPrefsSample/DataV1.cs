using UnityEngine;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class DataV1
    {
        public float Value;

        public override string ToString()
        {
            return Value.ToString("F2");
        }
    }
}
