using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Calluna.Persistence
{
    public interface IGameDataMigrator
    {
        int Version { get; }
        void Migrate(Dictionary<string, JToken> data);
    }
}
