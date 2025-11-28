namespace Calluna.Persistence.Samples.GameData
{
    public class Foo
    {
        public string Id;
        public bool Value;

        public override string ToString()
        {
            return $"Foo Id: {Id}, Value: {Value}";
        }
    }
}
