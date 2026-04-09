using UnityEditor;
using UnityEngine;

namespace Calluna.Persistence.Editor
{
    internal class GameDataViewerWindow : EditorWindow
    {
        private const string _defaultKey = "__GameData__";

        private string _playerPrefsKey = _defaultKey;
        private string _serializedData = string.Empty;

        private Vector2 _scrollPos;
        private string _statusMessage = string.Empty;
        private MessageType _statusType = MessageType.Info;

        [MenuItem("Calluna/Game Data Viewer")]
        private static void ShowWindow()
        {
            var window = GetWindow<GameDataViewerWindow>("Game Data Viewer");
            window.minSize = new Vector2(500, 300);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PlayerPrefs GameData Viewer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Key field
            EditorGUILayout.LabelField("PlayerPrefs Key");
            using (new EditorGUILayout.HorizontalScope())
            {
                _playerPrefsKey = EditorGUILayout.TextField(_playerPrefsKey);

                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    _playerPrefsKey = _defaultKey;
                }
            }

            EditorGUILayout.Space();

            // Load / Save buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load from PlayerPrefs"))
                {
                    LoadFromPlayerPrefs();
                }

                if (GUILayout.Button("Save clipboard → PlayerPrefs"))
                {
                    SaveClipboardToPlayerPrefs();
                }
            }

            EditorGUILayout.Space();

            // Serialized data text area
            EditorGUILayout.LabelField("Serialized GameData (from PlayerPrefs)", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            _serializedData = EditorGUILayout.TextArea(
                _serializedData,
                GUILayout.ExpandHeight(true)
            );
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // Copy button
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !string.IsNullOrEmpty(_serializedData);
                if (GUILayout.Button("Copy to Clipboard"))
                {
                    EditorGUIUtility.systemCopyBuffer = _serializedData;
                    SetStatus("Serialized data copied to clipboard.", MessageType.Info);
                }

                GUI.enabled = true;
            }

            EditorGUILayout.Space();

            // Status message
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        private void LoadFromPlayerPrefs()
        {
            if (string.IsNullOrEmpty(_playerPrefsKey))
            {
                SetStatus("PlayerPrefs key is empty.", MessageType.Error);
                return;
            }

            if (PlayerPrefs.HasKey(_playerPrefsKey))
            {
                _serializedData = PlayerPrefs.GetString(_playerPrefsKey);
                SetStatus($"Loaded data from PlayerPrefs key \"{_playerPrefsKey}\".", MessageType.Info);
            }
            else
            {
                _serializedData = string.Empty;
                SetStatus($"No PlayerPrefs entry found for key \"{_playerPrefsKey}\".", MessageType.Warning);
            }
        }

        private void SaveClipboardToPlayerPrefs()
        {
            if (string.IsNullOrEmpty(_playerPrefsKey))
            {
                SetStatus("PlayerPrefs key is empty.", MessageType.Error);
                return;
            }

            var clipboardData = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboardData))
            {
                SetStatus("Clipboard is empty. Nothing to save.", MessageType.Warning);
                return;
            }

            PlayerPrefs.SetString(_playerPrefsKey, clipboardData);
            PlayerPrefs.Save();

            _serializedData = clipboardData;
            SetStatus($"Saved clipboard data to PlayerPrefs key \"{_playerPrefsKey}\".", MessageType.Info);
        }

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            Repaint();
        }
    }
}