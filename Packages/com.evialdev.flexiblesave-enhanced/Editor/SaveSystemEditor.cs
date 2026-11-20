using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlexibleSave.Enhanced.Editor
{
    [CustomEditor(typeof(SaveSystem))]
    public class SaveSystemEditor : UnityEditor.Editor
    {
        private SerializedProperty m_fileNameProp;
        private SerializedProperty m_folderProp;
        private SerializedProperty m_encryptionProp;
        private SerializedProperty m_useBackgroundWriteProp;

        private void OnEnable()
        {
            m_fileNameProp = serializedObject.FindProperty("m_fileName");
            m_folderProp = serializedObject.FindProperty("m_folder");
            m_encryptionProp = serializedObject.FindProperty("m_encryption");
            m_useBackgroundWriteProp = serializedObject.FindProperty("m_useBackgroundWrite");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Save File Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_folderProp, new GUIContent("Folder"));
            EditorGUILayout.PropertyField(m_fileNameProp, new GUIContent("File Name"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Security & Threading", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_encryptionProp, new GUIContent("Encryption"));

            if ((EncryptionMode)m_encryptionProp.enumValueIndex == EncryptionMode.Light)
            {
                EditorGUILayout.HelpBox(
                    "Light encryption protects against casual editing on rooted devices using the application identifier. " +
                    "It is not security against determined attackers. Keep Application.identifier unchanged across updates.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(m_useBackgroundWriteProp, new GUIContent("Use Background Write"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            SaveSystem.VerboseLogging = EditorGUILayout.Toggle("Verbose Logging", SaveSystem.VerboseLogging);

            var system = (SaveSystem)target;
            string fullPath = SaveSystem.ComputeFullPath(
                Application.persistentDataPath,
                m_folderProp.stringValue,
                m_fileNameProp.stringValue);
            EditorGUILayout.HelpBox("Full Save Path:\n" + fullPath, MessageType.None);

            if (SaveablePrefabs.Instance == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "No SaveablePrefabs registry asset found at Assets/Resources/FlexibleSave/SaveablePrefabs.asset. " +
                    "Prefab reinstantiation requires this asset.",
                    MessageType.Warning);
                if (GUILayout.Button("Create & Sync SaveablePrefabs Asset"))
                {
                    SaveablePrefabsEditor.CreateOrSelectAsset();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Play Mode Controls", EditorStyles.boldLabel);

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Status",
                    $"HasLoaded: {system.HasLoaded} | IsCorrupt: {system.IsCorrupt} | IsBusy: {system.IsBusy}");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Load"))
                {
                    system.Load();
                }

                GUI.enabled = system.HasLoaded && !system.IsCorrupt && !system.IsBusy;
                if (GUILayout.Button("Save"))
                {
                    system.Save();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Delete Save"))
                {
                    system.Delete();
                }

                if (GUILayout.Button("Reload Scene"))
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Runtime Save/Load/Delete controls are active in Play Mode.", MessageType.Info);
            }
        }
    }
}
