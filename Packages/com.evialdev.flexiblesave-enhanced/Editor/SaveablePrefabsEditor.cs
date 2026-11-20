using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    [CustomEditor(typeof(SaveablePrefabs))]
    public class SaveablePrefabsEditor : UnityEditor.Editor
    {
        public struct ConflictData
        {
            public SaveableEntity Obj1;
            public SaveableEntity Obj2;
            public string Id;
            public int Index1;
            public int Index2;
            public string Message;
        }

        private const string DefaultAssetPath = "Assets/Resources/FlexibleSave/SaveablePrefabs.asset";

        private SerializedProperty m_prefabsProp;
        private ReorderableList m_reorderableList;

        [MenuItem("Tools/Flexible Save Enhanced/Create SaveablePrefabs Asset")]
        public static SaveablePrefabs CreateOrSelectAsset()
        {
            SaveablePrefabs existing = SaveablePrefabs.Instance;
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return existing;
            }

            string[] guids = AssetDatabase.FindAssets("t:SaveablePrefabs");
            if (guids.Length > 0)
            {
                string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                var loaded = AssetDatabase.LoadAssetAtPath<SaveablePrefabs>(foundPath);
                if (loaded != null)
                {
                    Selection.activeObject = loaded;
                    EditorGUIUtility.PingObject(loaded);
                    return loaded;
                }
            }

            string dir = Path.GetDirectoryName(DefaultAssetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var created = CreateInstance<SaveablePrefabs>();
            AssetDatabase.CreateAsset(created, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            SyncAllPrefabs();
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
            return created;
        }

        private void OnEnable()
        {
            m_prefabsProp = serializedObject.FindProperty("m_prefabs");
            BuildReorderableList();
        }

        private void BuildReorderableList()
        {
            m_reorderableList = new ReorderableList(serializedObject, m_prefabsProp, true, true, true, true);
            m_reorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Registered Saveable Prefabs");
            };
            m_reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty element = m_prefabsProp.GetArrayElementAtIndex(index);
                rect.y += 2;
                float idWidth = Mathf.Min(230f, rect.width * 0.45f);
                Rect objRect = new Rect(rect.x, rect.y, rect.width - idWidth - 6f, EditorGUIUtility.singleLineHeight);
                Rect idRect = new Rect(objRect.xMax + 6f, rect.y, idWidth, EditorGUIUtility.singleLineHeight);

                EditorGUI.PropertyField(objRect, element, GUIContent.none);

                var entity = element.objectReferenceValue as SaveableEntity;
                if (entity == null)
                {
                    EditorGUI.LabelField(idRect, "<Missing / Null>", EditorStyles.miniLabel);
                }
                else
                {
                    string prefabId = entity.PrefabID;
                    EditorGUI.LabelField(idRect, string.IsNullOrEmpty(prefabId) ? "<Empty Prefab ID>" : prefabId, EditorStyles.miniLabel);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan & Sync Prefabs"))
            {
                SyncAllPrefabs();
                serializedObject.Update();
            }
            if (GUILayout.Button("Clear List"))
            {
                m_prefabsProp.ClearArray();
                serializedObject.ApplyModifiedProperties();
                ((SaveablePrefabs)target).InvalidateLookup();
                return;
            }
            EditorGUILayout.EndHorizontal();

            List<ConflictData> conflicts = GetIDConflicts((SaveablePrefabs)target);
            if (conflicts.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Prefab ID conflicts found! Multiple prefabs share the same Prefab ID.",
                    MessageType.Error);

                foreach (ConflictData conflict in conflicts)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(conflict.Obj1, typeof(SaveableEntity), false);
                    EditorGUILayout.ObjectField(conflict.Obj2, typeof(SaveableEntity), false);
                    GUI.enabled = true;
                    if (GUILayout.Button("New ID for 2nd", GUILayout.Width(110)))
                    {
                        GenerateNewPrefabId(conflict.Obj2);
                        ((SaveablePrefabs)target).InvalidateLookup();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();
            m_reorderableList?.DoLayoutList();

            if (serializedObject.ApplyModifiedProperties())
            {
                ((SaveablePrefabs)target).InvalidateLookup();
            }
        }

        public static void GenerateNewPrefabId(SaveableEntity prefab)
        {
            if (prefab == null) return;
            var so = new SerializedObject(prefab);
            so.Update();
            SerializedProperty prop = so.FindProperty("m_prefabID");
            if (prop != null)
            {
                Undo.RecordObject(prefab, "Generate New Prefab ID");
                prop.stringValue = Guid.NewGuid().ToString();
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(prefab);
            }
        }

        public static void SyncAllPrefabs()
        {
            SaveablePrefabs asset = SaveablePrefabs.Instance;
            if (asset == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:SaveablePrefabs");
                if (guids.Length > 0)
                {
                    asset = AssetDatabase.LoadAssetAtPath<SaveablePrefabs>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (asset == null)
            {
                return;
            }

            var discovered = new List<SaveableEntity>();
            var seenPrefabs = new HashSet<SaveableEntity>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                if (root.TryGetComponent<SaveableEntity>(out SaveableEntity entity) && seenPrefabs.Add(entity))
                {
                    discovered.Add(entity);
                }
            }

            discovered.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            var so = new SerializedObject(asset);
            so.Update();
            SerializedProperty listProp = so.FindProperty("m_prefabs");
            if (listProp == null) return;

            bool changed = listProp.arraySize != discovered.Count;
            if (!changed)
            {
                for (int i = 0; i < discovered.Count; i++)
                {
                    if (listProp.GetArrayElementAtIndex(i).objectReferenceValue != discovered[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            listProp.ClearArray();
            for (int i = 0; i < discovered.Count; i++)
            {
                listProp.InsertArrayElementAtIndex(i);
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = discovered[i];
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            asset.InvalidateLookup();
        }

        internal static void SyncIncremental(IReadOnlyList<string> importedPrefabPaths, bool pruneMissing)
        {
            SaveablePrefabs asset = SaveablePrefabs.Instance;
            if (asset == null)
            {
                return;
            }

            var so = new SerializedObject(asset);
            so.Update();
            SerializedProperty listProp = so.FindProperty("m_prefabs");
            if (listProp == null)
            {
                return;
            }

            bool changed = false;
            var existing = new HashSet<SaveableEntity>();

            if (pruneMissing)
            {
                for (int i = listProp.arraySize - 1; i >= 0; i--)
                {
                    var elem = listProp.GetArrayElementAtIndex(i).objectReferenceValue as SaveableEntity;
                    if (elem == null || !existing.Add(elem))
                    {
                        listProp.DeleteArrayElementAtIndex(i);
                        changed = true;
                    }
                }
            }
            else
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    var elem = listProp.GetArrayElementAtIndex(i).objectReferenceValue as SaveableEntity;
                    if (elem != null)
                    {
                        existing.Add(elem);
                    }
                }
            }

            if (importedPrefabPaths != null)
            {
                for (int i = 0; i < importedPrefabPaths.Count; i++)
                {
                    string path = importedPrefabPaths[i];
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null || !root.TryGetComponent<SaveableEntity>(out SaveableEntity entity))
                    {
                        continue;
                    }

                    if (existing.Add(entity))
                    {
                        int insertIdx = listProp.arraySize;
                        listProp.InsertArrayElementAtIndex(insertIdx);
                        listProp.GetArrayElementAtIndex(insertIdx).objectReferenceValue = entity;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                asset.InvalidateLookup();
            }
        }

        public static List<ConflictData> GetIDConflicts(SaveablePrefabs prefs = null)
        {
            var conflicts = new List<ConflictData>();
            prefs ??= SaveablePrefabs.Instance;
            if (prefs == null || prefs.Prefabs == null)
            {
                return conflicts;
            }

            List<SaveableEntity> prefabs = prefs.Prefabs;
            var seen = new Dictionary<string, (SaveableEntity entity, int index)>(StringComparer.Ordinal);

            for (int i = 0; i < prefabs.Count; i++)
            {
                SaveableEntity entity = prefabs[i];
                if (entity == null) continue;

                string id = entity.PrefabID;
                if (string.IsNullOrEmpty(id)) continue;

                if (seen.TryGetValue(id, out var first))
                {
                    conflicts.Add(new ConflictData
                    {
                        Obj1 = first.entity,
                        Obj2 = entity,
                        Id = id,
                        Index1 = first.index,
                        Index2 = i,
                        Message = $"Prefabs '{first.entity.name}' (index {first.index}) and '{entity.name}' (index {i}) share Prefab ID '{id}'."
                    });
                }
                else
                {
                    seen.Add(id, (entity, i));
                }
            }

            return conflicts;
        }
    }
}
