using System;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// A <see cref="SaveLoader"/> backed by <see cref="PlayerPrefs"/>. Writes them to disk after every
    /// change - within a batch (see <see cref="GameDataPersistence"/>) only once, when the batch is
    /// committed. A rolled back batch can't undo its changes, since PlayerPrefs has no transactions:
    /// they are just not written to disk yet.
    /// </summary>
    public class PlayerPrefsSaveLoader : SaveLoader, IMainThreadSaveLoader, IBatchableSaveLoader, Injectable
    {
        public event Action OnClear;
        
        private JsonSerializer _serializer;
        private bool _inBatch;

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
        }
        
        public bool Has(string id)
        {
            return PlayerPrefs.HasKey(id);
        }

        public T Load<T>(string key, T defaultValue = default)
        {
            Type type = typeof(T);

            if (!Has(key))
                return defaultValue;
            if(type == typeof(string))
                return (T)(object)PlayerPrefs.GetString(key);
            if(type == typeof(int))
                return (T)(object)PlayerPrefs.GetInt(key);
            if(type == typeof(float))
                return (T)(object)PlayerPrefs.GetFloat(key);
            if(type == typeof(bool))
                return (T)(object)(PlayerPrefs.GetInt(key) != 0);
            return _serializer.Deserialize<T>(PlayerPrefs.GetString(key));
        }

        public void Save<T>(string id, T value)
        {
            if(value is int intValue)
                PlayerPrefs.SetInt(id, intValue);
            else if(value is float floatValue)
                PlayerPrefs.SetFloat(id, floatValue);
            else if(value is bool boolValue)
                PlayerPrefs.SetInt(id, boolValue ? 1 : 0);
            else if(value is string stringValue)
                PlayerPrefs.SetString(id, stringValue);
            else
                PlayerPrefs.SetString(id, _serializer.Serialize(value));
            if (!_inBatch)
                PlayerPrefs.Save();
        }

        public void Delete(string id)
        {
            PlayerPrefs.DeleteKey(id);
        }

        public void Clear()
        {
            PlayerPrefs.DeleteAll();
            OnClear?.Invoke();
        }

        void IBatchableSaveLoader.BeginBatch() => _inBatch = true;

        void IBatchableSaveLoader.CommitBatch()
        {
            _inBatch = false;
            PlayerPrefs.Save();
        }

        void IBatchableSaveLoader.RollbackBatch() => _inBatch = false;
    }
}
