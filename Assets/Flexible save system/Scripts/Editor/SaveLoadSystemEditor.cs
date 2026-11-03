

namespace SaveLoadSystem.Editor
{
    using UnityEngine;
    using UnityEditor;
    using UnityEngine.SceneManagement;

    [CustomEditor(typeof(SaveLoadSystem))]
    public class SaveLoadSystemEditor : Editor
    {
        enum Saves
        {
            savegame_0,
            savegame_1,
            savegame_2,
            savegame_3,
            savegame_4,
            savegame_5,
            savegame_6,
            savegame_7,
            savegame_8,
            savegame_9,
            savegame_10,
        };

        static string savePath = "saves/";
        static Saves currentSave = Saves.savegame_0;

        static GUIStyle orange;


        void OnEnable()
        {
            try
            {
                orange = new GUIStyle(EditorStyles.label);
                orange.normal.textColor = Color.white;
            }
            catch (System.Exception)
            { }

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Save File Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_savePath"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_saveName"));

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Encryption Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_useEncryption"));
            
            if (serializedObject.FindProperty("m_useEncryption").boolValue)
            {
                EditorGUILayout.HelpBox("Automatic protection against casual save editing, not hackers. Keep the Application Identifier unchanged so restored saves remain readable after reinstall. Uninstall may delete local saves; backup or cloud storage is needed to preserve them. Old encrypted save formats are not supported.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug Controls (Play Mode Only)", EditorStyles.boldLabel);

            string fullPath = SaveLoadSystem.fullSavePath;
            EditorGUILayout.HelpBox("Full Save Path:\n" + fullPath, MessageType.Info);

            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save New"))
                {
                    SaveLoadSystem.SaveNew();
                }
                if (GUILayout.Button("Save (Overwrite)"))
                {
                    SaveLoadSystem.Save();
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Load"))
                {
                    SaveLoadSystem.Load();
                }
                if (GUILayout.Button("Delete Save File"))
                {
                    SaveLoadSystem.Delete();
                }
                if (GUILayout.Button("Reload Scene"))
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Debug controls are available only when the game is running.", MessageType.Info);
            }
        }

        void SetSaveName(Saves op)
        {
            string saveName = GetSaveName(op);
            serializedObject.FindProperty("m_saveName").stringValue = saveName;
            serializedObject.ApplyModifiedProperties();
        }
        string GetSaveName(Saves op)
        {
            int index = (int)op;
            return "Save_" + index + ".save";
        }
    }
}
