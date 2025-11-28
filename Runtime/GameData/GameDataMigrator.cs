using System.Collections.Generic;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public abstract class GameDataMigrator : MonoBehaviour, Injectable
    {
        public abstract int Version { get; }

        public abstract void Migrate(Dictionary<string, string> data);

        protected JsonSerializer _serializer;
        
        public virtual void Inject(Resolver resolver)
        {
            _serializer = new JsonSerializer();
        }
    }
}
