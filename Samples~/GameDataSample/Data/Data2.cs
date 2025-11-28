namespace Calluna.Persistence.Samples.GameData
{
    public class Data2
    {
        public static string Id => "Data2";
        public Bar Bar;

        public override string ToString()
        {
            return $"Data2: Id: {Id}, Bar: {Bar}";
        }
    }
}
