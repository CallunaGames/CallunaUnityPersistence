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
            _gameDataPersistence.Save();
        }
    }
}