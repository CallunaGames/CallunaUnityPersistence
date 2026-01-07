using System;
using UnityEngine;

namespace Calluna.Persistence.Samples.PersistentDataPath
{
    [Serializable]
    public class Foo
    {
        public string Id;
        public float Value;

        public override string ToString()
        {
            return $"Foo (Id: {Id}, Value: {Value})";
        }
    }
}