using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    [InitializeOnLoad]
    public static class StartupUtilities
    {
        public static event Action UpdateIds;

        static StartupUtilities()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                GenerateEmptySceneAutoIds();
                CheckForIdConflicts();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                UpdateIds?.Invoke();
            }
        }

        private static void GenerateEmptySceneAutoIds()
        {
            UpdateIds?.Invoke();

            SaveableEntity[] entities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < entities.Length; i++)
            {
                SaveableEntity entity = entities[i];
                if (entity == null || entity.IsManualId || !string.IsNullOrEmpty(entity.ID))
                {
                    continue;
                }

                var so = new SerializedObject(entity);
                so.Update();
                SerializedProperty idProp = so.FindProperty("m_ID");
                if (idProp != null && string.IsNullOrEmpty(idProp.stringValue))
                {
                    idProp.stringValue = Guid.NewGuid().ToString();
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(entity);
                }
            }
        }

        public static void CheckForIdConflicts()
        {
            CheckForPrefabConflicts();
            CheckForScriptableObjectConflicts();
            CheckForSceneEntityConflicts();
        }

        private static void CheckForPrefabConflicts()
        {
            if (SaveablePrefabs.Instance == null)
            {
                SaveableEntity[] sceneEntities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                bool hasPrefabEntityInScene = false;
                for (int i = 0; i < sceneEntities.Length; i++)
                {
                    if (sceneEntities[i] != null && !string.IsNullOrEmpty(sceneEntities[i].PrefabID))
                    {
                        hasPrefabEntityInScene = true;
                        break;
                    }
                }

                if (hasPrefabEntityInScene)
                {
                    Debug.LogWarning(
                        "[FlexibleSave] Scene contains prefab SaveableEntity objects, but 'Assets/Resources/FlexibleSave/SaveablePrefabs.asset' was not found. " +
                        "Run 'Tools/Flexible Save Enhanced/Create SaveablePrefabs Asset' to enable prefab reinstantiation.");

                    if (!Application.isBatchMode &&
                        EditorUtility.DisplayDialog(
                            "Flexible Save Enhanced: SaveablePrefabs Missing",
                            "This scene uses prefab SaveableEntity objects, but no SaveablePrefabs registry asset exists yet.\n\nCreate and sync 'Assets/Resources/FlexibleSave/SaveablePrefabs.asset' now?",
                            "Create & Sync Now",
                            "Ignore"))
                    {
                        SaveablePrefabsEditor.CreateOrSelectAsset();
                    }
                }
                return;
            }

            List<SaveablePrefabsEditor.ConflictData> conflicts = SaveablePrefabsEditor.GetIDConflicts();
            if (conflicts.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < conflicts.Count; i++)
            {
                sb.AppendLine(conflicts[i].Message);
            }

            Debug.LogError($"[FlexibleSave] Prefab ID conflicts detected:\n{sb}");

            if (!Application.isBatchMode &&
                EditorUtility.DisplayDialog(
                    "Flexible Save Enhanced: Prefab ID Conflict",
                    "Duplicate Prefab IDs were detected:\n\n" + sb,
                    "Open SaveablePrefabs Asset",
                    "Ignore"))
            {
                if (SaveablePrefabs.Instance != null)
                {
                    Selection.activeObject = SaveablePrefabs.Instance;
                    EditorGUIUtility.PingObject(SaveablePrefabs.Instance);
                }
            }
        }

        private static void CheckForScriptableObjectConflicts()
        {
            TypeCache.TypeCollection saveIdTypes = TypeCache.GetTypesDerivedFrom<ISaveID>();
            if (saveIdTypes.Count == 0)
            {
                return;
            }

            var seen = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            var visited = new HashSet<ScriptableObject>();
            var conflicts = new StringBuilder();
            int conflictCount = 0;

            for (int t = 0; t < saveIdTypes.Count; t++)
            {
                Type type = saveIdTypes[t];
                if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (asset == null || !visited.Add(asset) || !(asset is ISaveID saveId))
                    {
                        continue;
                    }

                    string id = saveId.GetID();
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(id, out ScriptableObject first))
                    {
                        conflictCount++;
                        conflicts.AppendLine($"{asset.name} & {first.name} share ID '{id}'");
                        Debug.LogWarning($"[FlexibleSave] ScriptableObject ID conflict: '{asset.name}' and '{first.name}' share ID '{id}'.", asset);
                    }
                    else
                    {
                        seen.Add(id, asset);
                    }
                }
            }

            if (conflictCount > 0 && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Flexible Save Enhanced: ScriptableObject ID Conflict",
                    conflicts.ToString(),
                    "OK");
            }
        }

        private static void CheckForSceneEntityConflicts()
        {
            SaveableEntity[] entities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            var seen = new Dictionary<string, SaveableEntity>(StringComparer.Ordinal);
            var conflicts = new StringBuilder();
            int conflictCount = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                SaveableEntity entity = entities[i];
                if (entity == null) continue;

                string id = entity.ID;
                if (string.IsNullOrEmpty(id))
                {
                    if (entity.IsManualId)
                    {
                        Debug.LogError($"[FlexibleSave] SaveableEntity '{entity.name}' has Manual ID enabled but its ID is empty.", entity);
                    }
                    continue;
                }

                if (seen.TryGetValue(id, out SaveableEntity first))
                {
                    conflictCount++;
                    conflicts.AppendLine($"'{entity.name}' and '{first.name}' share ID '{id}'");
                    Debug.LogError(
                        $"[FlexibleSave] Duplicate SaveableEntity ID '{id}' on '{entity.name}' and '{first.name}'.",
                        entity);
                }
                else
                {
                    seen.Add(id, entity);
                }
            }

            if (conflictCount > 0 && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Flexible Save Enhanced: Scene Entity ID Conflict",
                    "Duplicate SaveableEntity IDs found in scene:\n\n" + conflicts,
                    "OK");
            }
        }
    }
}
