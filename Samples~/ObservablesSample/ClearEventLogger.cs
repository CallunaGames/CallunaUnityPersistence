using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.Samples.Observables
{
    /// <summary>
    /// Demonstrates subscribing to SaveLoader.OnClear.
    ///
    /// OnClear fires after Clear() erases all saved data from the underlying storage.
    /// Use it to reset any in-memory state that mirrors the saved data, or to notify
    /// the player that their progress has been wiped.
    /// </summary>
    public class ClearEventLogger : MonoBehaviour, Injectable
    {
        private SaveLoader _saveLoader;

        public void Inject(Resolver resolver)
        {
            _saveLoader = resolver.Resolve<SaveLoader>();
            _saveLoader.OnClear += OnCleared;
        }

        private void OnDestroy()
        {
            _saveLoader.OnClear -= OnCleared;
        }

        private void OnCleared()
        {
            Debug.Log("SaveLoader.Clear() was called. All save data has been wiped.");
        }
    }
}
