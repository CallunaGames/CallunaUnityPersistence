using System.Collections.Generic;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence.Samples.GameData
{
    public class GameDataTest : MonoBehaviour, Injectable, Cleanable
    {
        [SerializeField] private float _data1Value = 12.3f;
        [SerializeField] private string _data2Id = "MyFoo";
        [SerializeField] private bool _data2Value = false;
        
        private JsonSerializer _serializer;
        private GameDataPersistence _persistence;
        
        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            _persistence = resolver.Resolve<GameDataPersistence>();
        }

        public void Clean()
        {
            Dictionary<string, JToken> data = new Dictionary<string, JToken>();
            data.Add(Data1.Id, _serializer.SerializeToToken(new Data1_v0(){Value = _data1Value}));
            data.Add(Data2.Id, _serializer.SerializeToToken(new Data2_v0(){Foo = new Foo()
            {
                Id = _data2Id,
                Value = _data2Value
            }}));
            _persistence.OverrideSave(data, 0);
            Debug.Log($"Overridden Game data with {data[Data1.Id]} and {data[Data2.Id]}");
        }
    }
}
