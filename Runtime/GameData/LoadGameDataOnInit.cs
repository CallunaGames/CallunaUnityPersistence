using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    internal class LoadGameDataOnInit : MonoBehaviour, Injectable, Initializable
    {
        private GameDataPersistence _gameDataPersistence;
        
        public void Inject(Resolver resolver)
        {
            _gameDataPersistence = resolver.Resolve<GameDataPersistence>();
        }

        public void Initialize()
        {
            _gameDataPersistence.Load();
        }
    }
}
