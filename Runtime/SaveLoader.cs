using System;

namespace Calluna.Persistence
{
    public interface SaveLoader
    {
        public event Action OnClear;
        
        public bool Has(string id);
        public T Load<T>(string id, T defaultValue = default(T));
        public void Save<T>(string id, T value);
        public void Delete(string id);

        public void Clear();
    }
}
