using UnityEngine;
using UnityEngine.UI;

namespace Calluna.Persistence.PlayerPrefsSample
{
    public class SaveButton : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private PlayerPrefsDataLoader _testSaveLoader;
        
        private void Start()
        {
            _button.onClick.AddListener(Save);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(Save);
        }

        private void Save()
        {
            _testSaveLoader.Save();
        }
    }
}
