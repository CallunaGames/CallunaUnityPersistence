using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data2Migrator_v0Tov1 : GameDataMigrator
    {
        public override int Version => 2;
        public override void Migrate(Dictionary<string, JToken> data)
        {
            Data2_v0 formerData = _serializer.Deserialize<Data2_v0>(data[Data2.Id]);
            Data2 newData = new Data2(){Bar = new Bar(){IdAndValue = $"{formerData.Foo.Id} {formerData.Foo.Value}"}};
            data[Data2.Id] = _serializer.SerializeToToken(newData);
            Debug.Log($"Migrating {formerData} to {newData}");
        }
    }
}