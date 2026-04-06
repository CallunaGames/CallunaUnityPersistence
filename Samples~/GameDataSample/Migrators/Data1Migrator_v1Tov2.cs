using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data1Migrator_v1Tov2 : GameDataMigrator
    {
        public override int Version => 3;
        public override void Migrate(Dictionary<string, JToken> data)
        {
            Data1_v1 formerData = _serializer.Deserialize<Data1_v1>(data[Data1.Id]);
            Data2 data2 = _serializer.Deserialize<Data2>(data[Data2.Id]);
            Data1 newData = new Data1()
            {
                Bar = data2.Bar,
                Value = formerData.Value.ToString()
            };
            data[Data1.Id] = _serializer.SerializeToToken(newData);
            Debug.Log($"Migrating {formerData} to {newData}");
        }
    }
}