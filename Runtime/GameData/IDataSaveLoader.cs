using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    public interface IDataSaveLoader
    {
        string DataId { get; }
        void Load(JToken value);
        void LoadDefault();
        JToken GetSerializedData();
    }
}
