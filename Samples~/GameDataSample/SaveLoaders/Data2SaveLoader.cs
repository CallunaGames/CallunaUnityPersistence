using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data2SaveLoader : DataSaveLoader<Data2>
    {
        public override string DataId => Data2.Id;

        protected override void HandleLoadedData(Data2 data)
        {
            Debug.Log($"Loaded {data}");
        }

        protected override Data2 GetDefaultData()
        {
            return new Data2() { Bar = new Bar { IdAndValue = "Default" } };
        }

        protected override Data2 GetData()
        {
            Data2 data = new Data2() { Bar = new Bar() { IdAndValue = "Bar42 with value 3" } };
            Debug.Log($"Saving {data}");
            return data;
        }
    }
}