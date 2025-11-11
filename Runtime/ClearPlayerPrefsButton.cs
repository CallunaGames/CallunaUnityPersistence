using System;
using Calluna.DI;
using UnityEngine;
using UnityEngine.UI;

namespace Calluna.Persistence
{
    public class ClearPlayerPrefsButton : MonoBehaviour, Injectable
    {
        [SerializeField] private Button _button;

        private SaveLoader _saveLoader;
        
        public void Inject(Resolver resolver)
        {
            _saveLoader = resolver.Resolve<SaveLoader>();
        }
        
        private void Start()
        {
            _button.onClick.AddListener(ClearData);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(ClearData);
        }

        private void ClearData()
        {
            _saveLoader.Clear();
        }
    }
}
