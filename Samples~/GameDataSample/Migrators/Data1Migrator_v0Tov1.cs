using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data1Migrator_v0Tov1 : GameDataMigrator
    {
        public override int Version => 1;
        
        public override void Migrate(Dictionary<string, JToken> data)
        {
            Data1_v0 formerData = _serializer.Deserialize<Data1_v0>(data[Data1.Id]);
            Data1_v1 newData = new Data1_v1(){Value = Mathf.RoundToInt(formerData.Value)};
            data[Data1.Id] = _serializer.SerializeToToken(newData);
            Debug.Log($"Migrating {formerData} to {newData}");
        }
    }
}
