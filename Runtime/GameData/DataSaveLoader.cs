using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public abstract class DataSaveLoader : MonoBehaviour
    {
        public abstract string DataId { get; }
        public abstract void Load(string value);
        public abstract void LoadDefault();
        public abstract string GetSerializedData();
    }

    public abstract class DataSaveLoader<TData> : DataSaveLoader, Injectable
    {
        private JsonSerializer _serializer;
        
        public virtual void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
        }

        public override void Load(string value)
        {
            TData data = _serializer.Deserialize<TData>(value);
            HandleLoadedData(data);
        }

        public override void LoadDefault()
        {
            HandleLoadedData(GetDefaultData());
        }

        public override string GetSerializedData()
        {
            TData data = GetData();
            return _serializer.Serialize(data);
        }

        protected abstract void HandleLoadedData(TData data);
        protected abstract TData GetDefaultData();
        protected abstract TData GetData();
    }
}
