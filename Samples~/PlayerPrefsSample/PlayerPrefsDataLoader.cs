using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class PlayerPrefsDataLoader : MonoBehaviour, Injectable
    {
        private SaveLoader _saveLoader;
        private VersionedDataSaveLoader _versionedDataSaveLoader;

        private const string _dataId = "Data";
        private const string _numberId = "Number";
        private const string _floatId = "Float";
        private const string _boolId = "Boolean";
        private const string _stringId = "String";

        [SerializeField] private int _dataNumber = 101;
        [SerializeField] private int _number = 10;
        [SerializeField] private float _floatingNumber = 1.23f;
        [SerializeField] private bool _boolean = false;
        [SerializeField] private string _string = "My value";

        public void Inject(Resolver resolver)
        {
            _saveLoader = resolver.Resolve<SaveLoader>();
            _versionedDataSaveLoader = resolver.Resolve<VersionedDataSaveLoader>();
        }

        public void Load()
        {
            DataV2 data = _versionedDataSaveLoader.Load(_dataId, new DataV2() { Value = string.Empty });
            int number = _saveLoader.Load(_numberId, _number);
            Debug.Log($"Loaded data {data.Value} | {number} | {_floatingNumber} | {_boolean} | {_string}");
        }

        public void Save()
        {
            DataV0 dataV0 = new DataV0() { Value = _dataNumber };
            _versionedDataSaveLoader.Save(_dataId, dataV0, 0);
            _saveLoader.Save(_numberId, _number);
            _saveLoader.Save(_floatId, _floatingNumber);
            _saveLoader.Save(_boolId, _boolean);
            _saveLoader.Save(_stringId, _string);
            Debug.Log($"Saved data {dataV0.Value} | {_number} | {_floatingNumber} | {_boolean} | {_string}");
        }
    }
}