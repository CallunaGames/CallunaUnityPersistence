using UnityEngine;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class DataMigrationSteps
    {
        public class DataV0ToV1 : DataMigrationStep<DataV0, DataV1>
        {
            public override int TargetVersion => 1;
            
            protected override DataV1 Migrate(DataV0 data)
            {
                DataV1 result = new DataV1() { Value = data.Value + 0.34f };
                Debug.Log($"Migrating {data} to {result}");
                return result;
            }
        }
        
        public class DataV1ToV2 : DataMigrationStep<DataV1, DataV2>
        {
            public override int TargetVersion => 2;
            
            protected override DataV2 Migrate(DataV1 data)
            {
                DataV2 result = new DataV2() { Value = "Data Value: " + data.Value };
                Debug.Log($"Migrating {data} to {result}");
                return result;
            }
        }
    }
}
