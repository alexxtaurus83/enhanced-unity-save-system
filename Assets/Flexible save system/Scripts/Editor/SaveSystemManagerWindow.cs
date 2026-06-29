using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using SaveLoadSystem;
using Newtonsoft.Json;

namespace SaveLoadSystem.Editor
{
    public class SaveSystemManagerWindow : EditorWindow
    {
        private int _toolbarIndex = 0;
        private readonly string[] _toolbarLabels = { "Live Dashboard", "Save File Inspector" };
        private Vector2 _scrollPosition;
        private Dictionary<string, SaveableEntity> _allSaveables;

        [MenuItem("Tools/Flexible Save System/Manager Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<SaveSystemManagerWindow>("Save System Manager");
            if (!CheckForConflicts(false))
            {
                EditorUtility.DisplayDialog("ID Conflicts Found", "Duplicate IDs were found in the scene. Please check the console for details.", "OK");
            }
        }

        private void OnEnable()
        {
            EditorApplication.update += UpdateData;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateData;
        }

        void UpdateData()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, _toolbarLabels);

            switch (_toolbarIndex)
            {
                case 0:
                    DrawLiveDashboard();
                    break;
                case 1:
                    DrawSaveFileInspector();
                    break;
            }
        }

        private void DrawLiveDashboard()
        {
            EditorGUILayout.LabelField("Live Scene Data", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Live Dashboard is only available in Play Mode.", MessageType.Info);
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Scene ID Management", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Generate IDs for All"))
                {
                    GenerateIdsForAll(true);
                }
                if (GUILayout.Button("Generate for Missing IDs"))
                {
                    GenerateIdsForAll(false);
                }
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Check for ID Conflicts"))
                {
                    CheckForConflicts(true);
                }

                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                try { SaveLoadSystem.Save(); }
                catch (System.Exception e) { Debug.LogError($"Save operation failed: {e.Message}"); }
            }
            if (GUILayout.Button("Load"))
            {
                try { SaveLoadSystem.Load(); }
                catch (System.Exception e) { Debug.LogError($"Load operation failed: {e.Message}"); }
            }
            if (GUILayout.Button("Delete Save File"))
            {
                try { SaveLoadSystem.Delete(); }
                catch (System.Exception e) { Debug.LogError($"Delete operation failed: {e.Message}"); }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Save Path:", SaveLoadSystem.fullSavePath);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Registered Saveable Entities:", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("GameObject", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Entity ID", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            FetchSaveables();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_allSaveables == null || _allSaveables.Count == 0)
            {
                EditorGUILayout.LabelField("No saveable entities registered.");
            }
            else
            {
                EditorGUILayout.LabelField("Scene Objects", EditorStyles.boldLabel);
                foreach (var kvp in _allSaveables)
                {
                    if (kvp.Value == null || PrefabUtility.IsPartOfPrefabInstance(kvp.Value.gameObject)) continue;
                    DrawEntityRow(kvp);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Instantiated Prefabs", EditorStyles.boldLabel);
                foreach (var kvp in _allSaveables)
                {
                    if (kvp.Value == null || !PrefabUtility.IsPartOfPrefabInstance(kvp.Value.gameObject)) continue;
                    DrawEntityRow(kvp);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntityRow(KeyValuePair<string, SaveableEntity> kvp)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(kvp.Value.gameObject, typeof(GameObject), true);
            EditorGUI.EndDisabledGroup();

            string newId = EditorGUILayout.TextField(kvp.Key);
            if (newId != kvp.Key)
            {
                kvp.Value.SetID(newId);
            }

            if (GUILayout.Button("New ID", GUILayout.Width(60)))
            {
                kvp.Value.GenerateID();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void FetchSaveables()
        {
            var managerType = typeof(SaveableEntity.SaveableEntityManager);
            var fieldInfo = managerType.GetField("m_allSaveables", BindingFlags.NonPublic | BindingFlags.Static);
            if (fieldInfo != null)
            {
                _allSaveables = (Dictionary<string, SaveableEntity>)fieldInfo.GetValue(null);
            }
        }

        private Dictionary<string, object> _saveData;
        private Vector2 _inspectorScrollPosition;
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        private void DrawSaveFileInspector()
        {
            EditorGUILayout.LabelField("Inspect Save File", EditorStyles.boldLabel);

            if (GUILayout.Button("Load Save File"))
            {
                string path = EditorUtility.OpenFilePanel("Select Save File", SaveLoadSystem.basePath + SaveLoadSystem.savePath, "save");
                if (!string.IsNullOrEmpty(path))
                {
                    LoadAndParseSaveFile(path);
                }
            }

            if (_saveData != null)
            {
                EditorGUILayout.LabelField("File Content:", EditorStyles.boldLabel);
                _inspectorScrollPosition = EditorGUILayout.BeginScrollView(_inspectorScrollPosition);
                DrawDictionary(_saveData);
                EditorGUILayout.EndScrollView();
            }
        }

        private void LoadAndParseSaveFile(string path)
        {
            try
            {
                string json = System.IO.File.ReadAllText(path);
                
#if UNITY_2023_1_OR_NEWER
                SaveLoadSystem saveSystemInstance = FindFirstObjectByType<SaveLoadSystem>();
#else
                SaveLoadSystem saveSystemInstance = FindObjectOfType<SaveLoadSystem>();
#endif
                if (saveSystemInstance != null)
                {
                    var useEncryptionField = typeof(SaveLoadSystem).GetField("m_useEncryption", BindingFlags.NonPublic | BindingFlags.Instance);
                    var encryptionKeyField = typeof(SaveLoadSystem).GetField("m_encryptionKey", BindingFlags.NonPublic | BindingFlags.Instance);

                    if ((bool)useEncryptionField.GetValue(saveSystemInstance))
                    {
                        json = EncryptionUtility.Decrypt(json, (string)encryptionKeyField.GetValue(saveSystemInstance));
                    }
                }

                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ContractResolver = new PrivateFieldContractResolver()
                };
                _saveData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, settings);
            }
            catch (System.Exception e)
            {
                _saveData = null;
                Debug.LogError($"Failed to load or parse save file: {e.Message}");
            }
        }

        private void DrawDictionary(Dictionary<string, object> dictionary, string prefix = "")
        {
            if (dictionary == null) return;
            foreach (var kvp in dictionary)
            {
                DrawObject(kvp.Key, kvp.Value, 1, $"{prefix}.{kvp.Key}");
            }
        }

        private void DrawObject(string key, object value, int indent, string path)
        {
            EditorGUI.indentLevel = indent;

            if (!_foldoutStates.ContainsKey(path))
            {
                _foldoutStates[path] = false;
            }

            if (value is Dictionary<string, object> dict)
            {
                _foldoutStates[path] = EditorGUILayout.Foldout(_foldoutStates[path], key);
                if (_foldoutStates[path])
                {
                    foreach (var kvp in dict)
                    {
                        DrawObject(kvp.Key, kvp.Value, indent + 1, $"{path}.{kvp.Key}");
                    }
                }
            }
            else if (value is List<object> list)
            {
                _foldoutStates[path] = EditorGUILayout.Foldout(_foldoutStates[path], $"{key} [List, {list.Count} items]");
                if (_foldoutStates[path])
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        DrawObject($"Element {i}", list[i], indent + 1, $"{path}[{i}]");
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField(key, value?.ToString() ?? "null");
            }
        }

        private void GenerateIdsForAll(bool forceNew)
        {
            SaveableEntity[] allEntities = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var entity in allEntities)
            {
                if (forceNew || string.IsNullOrEmpty(entity.GetID()))
                {
                    Undo.RecordObject(entity, "Generate ID");
                    entity.GenerateID();
                    count++;
                    EditorUtility.SetDirty(entity);
                }
            }
            Debug.Log($"Generated IDs for {count} objects.");
        }

        private static bool CheckForConflicts(bool logIfNoConflicts)
        {
            SaveableEntity[] allEntities = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var idMap = new Dictionary<string, SaveableEntity>();
            bool conflictFound = false;
            foreach (var entity in allEntities)
            {
                string id = entity.GetID();
                if (string.IsNullOrEmpty(id)) continue;

                if (idMap.ContainsKey(id))
                {
                    conflictFound = true;
                    Debug.LogError($"ID Conflict: Object '{entity.name}' has the same ID '{id}' as '{idMap[id].name}'.", entity.gameObject);
                    Debug.LogError($"ID Conflict: Object '{idMap[id].name}' has the same ID '{id}' as '{entity.name}'.", idMap[id].gameObject);
                }
                else
                {
                    idMap.Add(id, entity);
                }
            }

            if (!conflictFound && logIfNoConflicts)
            {
                Debug.Log("No ID conflicts found in the current scene.");
            }
            return !conflictFound;
        }
    }
}