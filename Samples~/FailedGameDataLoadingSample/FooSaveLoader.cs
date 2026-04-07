using System;

namespace Calluna.Persistence.Samples.FailedGameDataLoading
{
    public class FooSaveLoader : DataSaveLoader<Foo>
    {
        public override string DataId => "Foo";
        
        protected override void HandleLoadedData(Foo data)
        {
            throw new Exception("FooSaveLoader failed to handle loaded data");
        }

        protected override Foo GetDefaultData()
        {
            return new Foo() { Id = "Id", Number = 42 };
        }

        protected override Foo GetData()
        {
            throw new Exception("FooSaveLoader failed to create data");
        }
    }
}
