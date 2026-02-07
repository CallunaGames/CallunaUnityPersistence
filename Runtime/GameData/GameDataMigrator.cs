using System.Collections.Generic;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    public abstract class GameDataMigrator : MonoBehaviour, Injectable
    {
        public abstract int Version { get; }

        public abstract void Migrate(Dictionary<string, JToken> data);

        protected JsonSerializer _serializer;
        
        public virtual void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
        }
    }
}
