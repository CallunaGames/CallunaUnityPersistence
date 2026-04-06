using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.Samples.Observables
{
    /// <summary>
    /// Demonstrates subscribing to GameDataPersistence.LoadingFailed and DataWasReset observables.
    ///
    /// LoadingFailed becomes true when an unrecoverable error occurs during Load().
    /// DataWasReset becomes true when the loaded data version is below LastSupportedVersion
    /// and all save data has been discarded.
    ///
    /// Both observables fire OnChanged whenever their value changes and expose the
    /// current value via .Value.
    /// </summary>
    public class GameDataStateLogger : MonoBehaviour, Injectable
    {
        private GameDataPersistence _persistence;

        public void Inject(Resolver resolver)
        {
            _persistence = resolver.Resolve<GameDataPersistence>();

            _persistence.LoadingFailed.OnChanged += OnLoadingFailedChanged;
            _persistence.DataWasReset.OnChanged += OnDataWasResetChanged;
        }

        private void OnDestroy()
        {
            _persistence.LoadingFailed.OnChanged -= OnLoadingFailedChanged;
            _persistence.DataWasReset.OnChanged -= OnDataWasResetChanged;
        }

        private void OnLoadingFailedChanged()
        {
            if (_persistence.LoadingFailed.Value)
                Debug.LogWarning("Game data failed to load. Show an error screen or fall back to defaults.");
        }

        private void OnDataWasResetChanged()
        {
            if (_persistence.DataWasReset.Value)
                Debug.Log("Save data was too old and has been reset. Notify the player.");
        }
    }
}
