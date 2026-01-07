using System.Collections.Generic;
using Calluna.DI;
using UnityEngine;
using UnityEngine.UI;

namespace Calluna.Persistence.Samples.PersistentDataPath
{
    public class SaveLoadTester : MonoBehaviour, Injectable, Initializable, Cleanable
    {
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _loadButton;
        [SerializeField] private Button _clearButton;
        
        [Space, SerializeField] private int _intValueToSave = 42;
        [SerializeField] private string _stringValueToSave = "Hello World";
        [SerializeField] private bool _boolValueToSave = true;
        [SerializeField] private float _floatValueToSave = 3.14f;
        [SerializeField] private List<int> _listToSave = new List<int>();
        [SerializeField] private Foo _customDataToSave = new Foo();
        
        private SaveLoader _saveLoader;
        
        public void Inject(Resolver resolver)
        {
            _saveLoader = resolver.Resolve<SaveLoader>();
        }

        public void Initialize()
        {
            _saveButton.onClick.AddListener(Save);
            _loadButton.onClick.AddListener(Load);
            _clearButton.onClick.AddListener(Clear);
        }

        public void Clean()
        {
            _saveButton.onClick.RemoveListener(Save);
            _loadButton.onClick.RemoveListener(Load);
        }

        private void Save()
        {
            _saveLoader.Save("int", _intValueToSave);
            _saveLoader.Save("string", _stringValueToSave);
            _saveLoader.Save("bool", _boolValueToSave);
            _saveLoader.Save("float", _floatValueToSave);
            _saveLoader.Save("list", _listToSave);
            _saveLoader.Save("customData", _customDataToSave);

            Debug.Log("Saved data");
        }

        private void Clear()
        {
            _saveLoader.Clear();
        }

        private void Load()
        {
            int intValue = _saveLoader.Load("int", _intValueToSave);
            string stringValue = _saveLoader.Load("string", _stringValueToSave);
            bool boolValue = _saveLoader.Load("bool", _boolValueToSave);
            float floatValue = _saveLoader.Load("float", _floatValueToSave);
            List<int> list = _saveLoader.Load("list", _listToSave);
            Foo foo = _saveLoader.Load("customData", _customDataToSave);
            
            Debug.Log($"Loaded data: int {intValue}, string: {stringValue}, bool: {boolValue}, float: {floatValue}, list: {list}, foo: {foo}");
        }
    }
}