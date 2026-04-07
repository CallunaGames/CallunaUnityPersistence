using System.Collections.Generic;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Migrates the composite GameData dictionary across versions. Used exclusively with <see cref="GameDataPersistence"/>.
    /// This is independent of <see cref="VersionedDataMigrator{T}"/>, which migrates individual typed values
    /// used with <see cref="VersionedDataSaveLoader"/>.
    /// </summary>
    public abstract class GameDataMigrator : MonoBehaviour, Injectable, IGameDataMigrator
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
