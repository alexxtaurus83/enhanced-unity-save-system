using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlexibleSave.Enhanced.Editor
{
    /// <summary>
    /// Base custom inspector for <see cref="ISaveID"/> components and ScriptableObjects (such as custom ScriptableObject IDs).
    /// </summary>
    public class SaveIDManagerEditor<T> : UnityEditor.Editor where T : Object, ISaveID
    {
        private static bool s_globalShowBase;
        protected bool showBase;

        private bool m_inPrefabMode;
        private bool m_hasPrefabProperty;
        private readonly List<T> m_cachedConflicts = new List<T>();
        private bool m_conflictsDirty = true;

        protected virtual void OnEnable()
        {
            StartupUtilities.UpdateIds += GenerateMissingIds;
            EditorApplication.hierarchyChanged -= MarkConflictsDirty;
            EditorApplication.hierarchyChanged += MarkConflictsDirty;
            EditorApplication.projectChanged -= MarkConflictsDirty;
            EditorApplication.projectChanged += MarkConflictsDirty;
            Undo.undoRedoPerformed -= MarkConflictsDirty;
            Undo.undoRedoPerformed += MarkConflictsDirty;
            m_conflictsDirty = true;

            SerializedProperty prefabProp = serializedObject.FindProperty("m_prefabID");
            m_hasPrefabProperty = prefabProp != null;

            bool isMonoBehaviour = target is MonoBehaviour;
            GameObject targetGo = target is Component comp ? comp.gameObject : null;
            bool isPersistent = EditorUtility.IsPersistent(target);
            bool inPrefabStage = targetGo != null && PrefabStageUtility.GetPrefabStage(targetGo) != null;

            if (isMonoBehaviour && (isPersistent || inPrefabStage))
            {
                m_inPrefabMode = true;
                SerializedProperty idProp = serializedObject.FindProperty("m_ID");
                if (idProp != null && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    serializedObject.Update();
                    idProp.stringValue = string.Empty;
                    serializedObject.ApplyModifiedProperties();
                }

                if (prefabProp != null && string.IsNullOrEmpty(prefabProp.stringValue))
                {
                    serializedObject.Update();
                    prefabProp.stringValue = Guid.NewGuid().ToString();
                    serializedObject.ApplyModifiedProperties();
                }
            }
            else if (!m_inPrefabMode)
            {
                GenerateRandomIdInternal(target, overwriteExisting: false);
            }
        }

        protected virtual void OnDisable()
        {
            StartupUtilities.UpdateIds -= GenerateMissingIds;
            EditorApplication.hierarchyChanged -= MarkConflictsDirty;
            EditorApplication.projectChanged -= MarkConflictsDirty;
            Undo.undoRedoPerformed -= MarkConflictsDirty;
        }

        protected void MarkConflictsDirty()
        {
            m_conflictsDirty = true;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            OnBeginInspectorGUI();

            T myTarget = (T)target;

            DrawLine(2);
            EditorGUILayout.LabelField("Flexible Save ID Management", EditorStyles.boldLabel);
            DrawLine(1);

            s_globalShowBase = EditorGUILayout.Toggle("Show Base Inspector", s_globalShowBase);

            SerializedProperty idProp = serializedObject.FindProperty("m_ID");
            SerializedProperty prefabProp = serializedObject.FindProperty("m_prefabID");

            if (!m_inPrefabMode)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Generate ID For");
                if (GUILayout.Button("This"))
                {
                    GenerateRandomId(myTarget);
                    m_conflictsDirty = true;
                }
                if (GUILayout.Button("All"))
                {
                    GenerateRandomIdForAll(GetObjects());
                    m_conflictsDirty = true;
                }
                if (GUILayout.Button("Missing Only"))
                {
                    GenerateMissingIds();
                    m_conflictsDirty = true;
                }
                EditorGUILayout.EndHorizontal();

                if (idProp != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(idProp, new GUIContent("ID"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_conflictsDirty = true;
                    }
                }

                if (m_hasPrefabProperty && prefabProp != null)
                {
                    EditorGUILayout.LabelField("Prefab ID", prefabProp.stringValue);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Scene instance IDs are cleared on Prefab assets.", MessageType.Info);
                if (m_hasPrefabProperty && prefabProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = false;
                    EditorGUILayout.TextField("Prefab ID", prefabProp.stringValue);
                    GUI.enabled = true;
                    if (GUILayout.Button("Copy", GUILayout.Width(55)))
                    {
                        GUIUtility.systemCopyBuffer = prefabProp.stringValue;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (m_conflictsDirty)
            {
                m_cachedConflicts.Clear();
                m_cachedConflicts.AddRange(GetIdConflicts(GetObjects()));
                m_conflictsDirty = false;
            }

            if (m_cachedConflicts.Count > 0)
            {
                DisplayConflicts(m_cachedConflicts);
            }

            serializedObject.ApplyModifiedProperties();
            DrawLine(2);

            OnEndInspectorGUI();

            if (showBase || s_globalShowBase)
            {
                base.OnInspectorGUI();
            }
        }

        public virtual void OnBeginInspectorGUI() { }
        public virtual void OnEndInspectorGUI() { }

        protected virtual T[] GetObjects()
        {
            return Array.Empty<T>();
        }

        private void GenerateMissingIds()
        {
            GenerateRandomIdForAllWithoutId(GetObjects());
            m_conflictsDirty = true;
        }

        public void GenerateRandomId(Object obj)
        {
            GenerateRandomIdInternal(obj, overwriteExisting: true);
        }

        public void GenerateRandomIdForAll(T[] objs)
        {
            if (objs == null) return;
            foreach (T item in objs)
            {
                if (item != null) GenerateRandomIdInternal(item, overwriteExisting: true);
            }
        }

        public void GenerateRandomIdForAllWithoutId(T[] objs)
        {
            if (objs == null) return;
            foreach (T item in objs)
            {
                if (item != null) GenerateRandomIdInternal(item, overwriteExisting: false);
            }
        }

        private static void GenerateRandomIdInternal(Object targetObj, bool overwriteExisting)
        {
            if (targetObj == null) return;
            var so = new SerializedObject(targetObj);
            so.Update();
            SerializedProperty idProp = so.FindProperty("m_ID");
            if (idProp == null) return;
            if (!overwriteExisting && !string.IsNullOrEmpty(idProp.stringValue)) return;

            Undo.RecordObject(targetObj, "Generate Save ID");
            idProp.stringValue = Guid.NewGuid().ToString();
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObj);
        }

        public static List<T> GetIdConflicts(T[] objs)
        {
            var conflicts = new List<T>();
            if (objs == null) return conflicts;

            var seen = new Dictionary<string, T>(StringComparer.Ordinal);
            var addedFirst = new HashSet<T>();

            for (int i = 0; i < objs.Length; i++)
            {
                T item = objs[i];
                if (item == null) continue;
                string id = item.GetID();
                if (string.IsNullOrEmpty(id)) continue;

                if (seen.TryGetValue(id, out T first))
                {
                    if (addedFirst.Add(first))
                    {
                        conflicts.Add(first);
                    }
                    conflicts.Add(item);
                }
                else
                {
                    seen.Add(id, item);
                }
            }
            return conflicts;
        }

        private static void DisplayConflicts(List<T> conflicts)
        {
            var msg = "Duplicate IDs detected:\n";
            for (int i = 0; i < conflicts.Count; i++)
            {
                T item = conflicts[i];
                if (item != null)
                {
                    msg += $" - {item.name} ({item.GetID()})\n";
                }
            }
            EditorGUILayout.HelpBox(msg.TrimEnd(), MessageType.Error);
        }

        private static void DrawLine(int height)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            rect.height = height;
            EditorGUI.DrawRect(rect, new Color(0.45f, 0.45f, 0.45f, 1f));
        }
    }
}
