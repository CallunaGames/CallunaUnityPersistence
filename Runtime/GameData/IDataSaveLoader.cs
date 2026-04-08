using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    public interface IDataSaveLoader
    {
        string DataId { get; }
        void Load(JToken value);
        void LoadDefault();
        JToken GetSerializedData();

        /// <summary>
        /// Returns true if this loader's data has changed since the last successful Save().
        /// Defaults to true so all loaders are saved unless they explicitly opt into dirty tracking.
        /// To opt in: override IsDirty and MarkClean() with your own flag, and set that flag to true
        /// whenever data is mutated.
        /// </summary>
        bool IsDirty => true;

        /// <summary>
        /// Called automatically by GameDataPersistence after a successful Save().
        /// Loaders that opt into dirty tracking should clear their dirty flag here.
        /// </summary>
        void MarkClean() { }
    }
}
