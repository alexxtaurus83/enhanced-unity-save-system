using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    internal sealed class PrefabAssetPostProcessor : AssetPostprocessor
    {
        private static readonly HashSet<string> s_pendingPrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_savingAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_pendingPrune;
        private static bool s_delayScheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool anyChange = false;

            for (int i = 0; i < importedAssets.Length; i++)
            {
                string path = importedAssets[i];
                if (!IsRelevantPrefabPath(path))
                {
                    continue;
                }

                if (s_savingAssetPaths.Remove(path))
                {
                    continue;
                }

                anyChange = true;
                s_pendingPrefabPaths.Add(path);
            }

            if (ContainsRelevantPrefab(deletedAssets))
            {
                s_pendingPrune = true;
                anyChange = true;
            }

            if (ContainsRelevantPrefab(movedAssets) || ContainsRelevantPrefab(movedFromAssetPaths))
            {
                anyChange = true;
            }

            if (anyChange)
            {
                ScheduleProcess();
            }
        }

        private static void ScheduleProcess()
        {
            if (s_delayScheduled)
            {
                return;
            }

            s_delayScheduled = true;
            EditorApplication.delayCall += ProcessPendingPrefabs;
        }

        private static void ProcessPendingPrefabs()
        {
            s_delayScheduled = false;

            string[] paths = null;
            if (s_pendingPrefabPaths.Count > 0)
            {
                paths = new string[s_pendingPrefabPaths.Count];
                s_pendingPrefabPaths.CopyTo(paths);
                s_pendingPrefabPaths.Clear();

                for (int i = 0; i < paths.Length; i++)
                {
                    EnsurePrefabChildIds(paths[i]);
                }
            }

            bool pruneMissing = s_pendingPrune;
            s_pendingPrune = false;

            if (paths != null || pruneMissing)
            {
                SaveablePrefabsEditor.SyncIncremental(paths, pruneMissing);
            }
        }

        private static bool ContainsRelevantPrefab(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
            {
                if (IsRelevantPrefabPath(paths[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsRelevantPrefabPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePrefabChildIds(string assetPath)
        {
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabRoot == null)
            {
                return;
            }

            SaveableEntity[] entities = prefabRoot.GetComponentsInChildren<SaveableEntity>(includeInactive: true);
            if (entities == null || entities.Length == 0)
            {
                return;
            }

            bool dirty = false;
            var seenChildIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < entities.Length; i++)
            {
                SaveableEntity entity = entities[i];
                if (entity == null) continue;

                var so = new SerializedObject(entity);
                so.Update();
                bool entityModified = false;

                SerializedProperty idProp = so.FindProperty("m_ID");
                SerializedProperty manualProp = so.FindProperty("m_manualId");
                if (idProp != null && manualProp != null && !manualProp.boolValue && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    idProp.stringValue = string.Empty;
                    entityModified = true;
                }

                if (entity.gameObject == prefabRoot)
                {
                    SerializedProperty prefabIdProp = so.FindProperty("m_prefabID");
                    if (prefabIdProp != null && string.IsNullOrEmpty(prefabIdProp.stringValue))
                    {
                        prefabIdProp.stringValue = Guid.NewGuid().ToString();
                        entityModified = true;
                    }
                }
                else
                {
                    SerializedProperty childIdProp = so.FindProperty("m_childID");
                    if (childIdProp != null)
                    {
                        string currentChildId = childIdProp.stringValue;
                        if (string.IsNullOrEmpty(currentChildId) || !seenChildIds.Add(currentChildId))
                        {
                            string fresh = Guid.NewGuid().ToString();
                            seenChildIds.Add(fresh);
                            childIdProp.stringValue = fresh;
                            entityModified = true;
                        }
                    }
                }

                if (entityModified)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(entity);
                    dirty = true;
                }
            }

            if (dirty)
            {
                s_savingAssetPaths.Add(assetPath);
                PrefabUtility.SavePrefabAsset(prefabRoot);
            }
        }
    }
}
