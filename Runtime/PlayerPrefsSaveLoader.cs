using System;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public class PlayerPrefsSaveLoader : SaveLoader, Injectable
    {
        public event Action OnClear;
        
        private JsonSerializer _serializer;

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
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteAll();
            OnClear?.Invoke();
        }
    }
}
