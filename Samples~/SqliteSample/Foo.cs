using System;

namespace Calluna.Persistence.Samples.Sqlite
{
    [Serializable]
    public class Foo
    {
        public string Id;
        public float Value;

        public override string ToString() => $"Foo (Id: {Id}, Value: {Value})";
    }
}
