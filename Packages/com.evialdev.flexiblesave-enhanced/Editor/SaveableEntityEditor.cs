using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    [CustomEditor(typeof(SaveableEntity))]
    public class SaveableEntityEditor : UnityEditor.Editor
    {
        private SerializedProperty m_idProp;
        private SerializedProperty m_manualIdProp;
        private SerializedProperty m_childIdProp;
        private SerializedProperty m_prefabIdProp;
        private SerializedProperty m_saveTransformProp;

        private string m_cachedValidationError;
        private bool m_validationDirty = true;

        private void OnEnable()
        {
            m_idProp = serializedObject.FindProperty("m_ID");
            m_manualIdProp = serializedObject.FindProperty("m_manualId");
            m_childIdProp = serializedObject.FindProperty("m_childID");
            m_prefabIdProp = serializedObject.FindProperty("m_prefabID");
            m_saveTransformProp = serializedObject.FindProperty("m_saveTransform");

            EditorApplication.hierarchyChanged -= MarkValidationDirty;
            EditorApplication.hierarchyChanged += MarkValidationDirty;
            Undo.undoRedoPerformed -= MarkValidationDirty;
            Undo.undoRedoPerformed += MarkValidationDirty;

            EnsureInitialState();
            m_validationDirty = true;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkValidationDirty;
            Undo.undoRedoPerformed -= MarkValidationDirty;
        }

        private void MarkValidationDirty()
        {
            m_validationDirty = true;
        }

        private void EnsureInitialState()
        {
            var entity = (SaveableEntity)target;
            if (entity == null) return;

            serializedObject.Update();
            bool changed = false;

            bool isPrefab = IsPrefabAssetOrStage(entity);
            if (isPrefab)
            {
                if (!m_manualIdProp.boolValue && !string.IsNullOrEmpty(m_idProp.stringValue))
                {
                    m_idProp.stringValue = string.Empty;
                    changed = true;
                }

                if (IsRootOfPrefab(entity) && string.IsNullOrEmpty(m_prefabIdProp.stringValue))
                {
                    m_prefabIdProp.stringValue = Guid.NewGuid().ToString();
                    changed = true;
                }

                if (!IsRootOfPrefab(entity) && string.IsNullOrEmpty(m_childIdProp.stringValue))
                {
                    m_childIdProp.stringValue = Guid.NewGuid().ToString();
                    changed = true;
                }
            }
            else if (!m_manualIdProp.boolValue && string.IsNullOrEmpty(m_idProp.stringValue))
            {
                Undo.RecordObject(entity, "Assign SaveableEntity ID");
                m_idProp.stringValue = Guid.NewGuid().ToString();
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(entity);
            }
        }

        public override void OnInspectorGUI()
        {
            var entity = (SaveableEntity)target;
            if (entity == null) return;

            serializedObject.Update();

            bool isPrefab = IsPrefabAssetOrStage(entity);

            EditorGUILayout.LabelField("Entity Identity", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_manualIdProp, new GUIContent("Manual ID"));
            if (EditorGUI.EndChangeCheck())
            {
                if (!m_manualIdProp.boolValue && !isPrefab && string.IsNullOrEmpty(m_idProp.stringValue))
                {
                    m_idProp.stringValue = Guid.NewGuid().ToString();
                }
                m_validationDirty = true;
            }

            if (isPrefab && !m_manualIdProp.boolValue)
            {
                EditorGUILayout.HelpBox("Prefab assets generate unique scene IDs automatically when instantiated.", MessageType.Info);
            }
            else
            {
                if (m_manualIdProp.boolValue)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(m_idProp, new GUIContent("ID"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_validationDirty = true;
                    }
                    if (GUILayout.Button("Copy", GUILayout.Width(55)))
                    {
                        GUIUtility.systemCopyBuffer = m_idProp.stringValue ?? string.Empty;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = false;
                    EditorGUILayout.TextField("ID", m_idProp.stringValue);
                    GUI.enabled = true;
                    if (GUILayout.Button("New", GUILayout.Width(45)))
                    {
                        if (Application.isPlaying)
                        {
                            entity.GenerateID();
                            serializedObject.Update();
                        }
                        else
                        {
                            Undo.RecordObject(entity, "Regenerate SaveableEntity ID");
                            m_idProp.stringValue = Guid.NewGuid().ToString();
                        }
                        m_validationDirty = true;
                    }
                    if (GUILayout.Button("Copy", GUILayout.Width(50)))
                    {
                        GUIUtility.systemCopyBuffer = m_idProp.stringValue ?? string.Empty;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (!isPrefab)
            {
                if (m_validationDirty)
                {
                    m_cachedValidationError = ValidateEntityId(entity, m_idProp.stringValue, m_manualIdProp.boolValue);
                    m_validationDirty = false;
                }

                if (!string.IsNullOrEmpty(m_cachedValidationError))
                {
                    EditorGUILayout.HelpBox(m_cachedValidationError, MessageType.Error);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Prefab & Transform", EditorStyles.boldLabel);

            if (isPrefab || !string.IsNullOrEmpty(m_prefabIdProp.stringValue))
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = false;
                EditorGUILayout.TextField("Prefab ID", m_prefabIdProp.stringValue);
                GUI.enabled = true;

                if (isPrefab && GUILayout.Button("New", GUILayout.Width(45)))
                {
                    Undo.RecordObject(entity, "Generate New Prefab ID");
                    m_prefabIdProp.stringValue = Guid.NewGuid().ToString();
                }

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    GUIUtility.systemCopyBuffer = m_prefabIdProp.stringValue ?? string.Empty;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(m_childIdProp.stringValue) || (isPrefab && !IsRootOfPrefab(entity)))
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = false;
                EditorGUILayout.TextField("Child ID", m_childIdProp.stringValue);
                GUI.enabled = true;
                if (isPrefab && GUILayout.Button("New", GUILayout.Width(45)))
                {
                    Undo.RecordObject(entity, "Generate New Child ID");
                    m_childIdProp.stringValue = Guid.NewGuid().ToString();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.PropertyField(m_saveTransformProp, new GUIContent("Save Transform"));

            serializedObject.ApplyModifiedProperties();
        }

        internal static string ValidateEntityId(SaveableEntity targetEntity, string id, bool isManual)
        {
            if (string.IsNullOrEmpty(id))
            {
                return isManual
                    ? "Manual ID is enabled, but the ID field is empty. Enter a unique ID."
                    : "Entity ID is empty.";
            }

            SaveableEntity[] sceneEntities = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sceneEntities.Length; i++)
            {
                SaveableEntity other = sceneEntities[i];
                if (other == null || other == targetEntity)
                {
                    continue;
                }

                if (string.Equals(other.ID, id, StringComparison.Ordinal))
                {
                    return $"Duplicate ID '{id}' conflicts with scene object '{other.name}'.";
                }
            }

            SaveablePrefabs prefabsAsset = SaveablePrefabs.Instance;
            if (prefabsAsset != null && prefabsAsset.Prefabs != null)
            {
                List<SaveableEntity> prefabs = prefabsAsset.Prefabs;
                for (int i = 0; i < prefabs.Count; i++)
                {
                    SaveableEntity prefab = prefabs[i];
                    if (prefab == null || prefab == targetEntity)
                    {
                        continue;
                    }

                    if (string.Equals(prefab.PrefabID, id, StringComparison.Ordinal) ||
                        string.Equals(prefab.ID, id, StringComparison.Ordinal))
                    {
                        return $"ID '{id}' conflicts with prefab '{prefab.name}' in SaveablePrefabs.";
                    }
                }
            }

            return null;
        }

        private static bool IsPrefabAssetOrStage(SaveableEntity entity)
        {
            if (entity == null) return false;
            return EditorUtility.IsPersistent(entity.gameObject) || PrefabStageUtility.GetPrefabStage(entity.gameObject) != null;
        }

        private static bool IsRootOfPrefab(SaveableEntity entity)
        {
            if (entity == null) return false;
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(entity.gameObject);
            if (stage != null)
            {
                return stage.prefabContentsRoot == entity.gameObject;
            }

            return entity.transform.parent == null;
        }
    }
}
