using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    public abstract class DataSaveLoader : MonoBehaviour, IDataSaveLoader
    {
        public abstract string DataId { get; }
        public abstract void Load(JToken value);
        public abstract void LoadDefault();
        public abstract JToken GetSerializedData();

        /// <inheritdoc cref="IDataSaveLoader.IsDirty"/>
        public virtual bool IsDirty => true;

        /// <inheritdoc cref="IDataSaveLoader.MarkClean"/>
        public virtual void MarkClean() { }
    }

    public abstract class DataSaveLoader<TData> : DataSaveLoader, Injectable
    {
        private JsonSerializer _serializer;
        
        public virtual void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
        }

        public override void Load(JToken value)
        {
            TData data = _serializer.Deserialize<TData>(value);
            HandleLoadedData(data);
        }

        public override void LoadDefault()
        {
            HandleLoadedData(GetDefaultData());
        }

        public override JToken GetSerializedData()
        {
            TData data = GetData();
            return _serializer.SerializeToToken(data);
        }

        protected abstract void HandleLoadedData(TData data);
        protected abstract TData GetDefaultData();
        protected abstract TData GetData();
    }
}
