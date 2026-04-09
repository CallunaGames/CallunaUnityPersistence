using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    internal class SaveGameDataOnClean : MonoBehaviour, Injectable, Cleanable
    {
        private GameDataPersistence _gameDataPersistence;
        
        public void Inject(Resolver resolver)
        {
            _gameDataPersistence = resolver.Resolve<GameDataPersistence>();
        }

        public void Clean()
        {
            // Ensure any in-progress background write from SaveAsync() completes before
            // the final synchronous save, so no data is lost on scene teardown.
            _gameDataPersistence.FlushPendingWrite();
            _gameDataPersistence.Save();
        }
    }
}