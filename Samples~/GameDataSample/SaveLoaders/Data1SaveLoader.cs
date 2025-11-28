using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class Data1SaveLoader : DataSaveLoader<Data1>
    {
        public override string DataId => Data1.Id;

        protected override void HandleLoadedData(Data1 data)
        {
            Debug.Log($"Loaded {data}");
        }

        protected override Data1 GetDefaultData()
        {
            return new Data1() { Bar = new Bar { IdAndValue = "Default" } };
        }

        protected override Data1 GetData()
        {
            Data1 data = new Data1()
            {
                Bar = new Bar() { IdAndValue = "Bar0 with value -22" },
                Value = "Data1 Magic Value"
            };
            Debug.Log($"Saving {data}");
            return data;
        }
    }
}