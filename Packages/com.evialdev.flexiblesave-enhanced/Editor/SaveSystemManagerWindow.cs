using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    public class SaveSystemManagerWindow : EditorWindow
    {
        private const double RepaintIntervalSeconds = 0.25;

        private int m_toolbarIndex;
        private readonly string[] m_toolbarLabels = { "Live Dashboard", "Save File Inspector" };
        private Vector2 m_dashboardScroll;
        private Vector2 m_inspectorScroll;
        private double m_nextRepaintTime;

        private readonly Dictionary<SaveableEntity, string> m_pendingIdEdits = new Dictionary<SaveableEntity, string>();
        private JObject m_inspectedRoot;
        private string m_inspectedFilePath;
        private string m_inspectorError;
        private readonly Dictionary<string, bool> m_foldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);

        [MenuItem("Tools/Flexible Save Enhanced/Manager Window")]
        public static void ShowWindow()
        {
            GetWindow<SaveSystemManagerWindow>("Flexible Save Manager");
        }

        private void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (Application.isPlaying && EditorApplication.timeSinceStartup >= m_nextRepaintTime)
            {
                m_nextRepaintTime = EditorApplication.timeSinceStartup + RepaintIntervalSeconds;
                Repaint();
            }
        }

        private void OnGUI()
        {
            m_toolbarIndex = GUILayout.Toolbar(m_toolbarIndex, m_toolbarLabels);
            EditorGUILayout.Space();

            if (m_toolbarIndex == 0)
            {
                DrawLiveDashboard();
            }
            else
            {
                DrawSaveFileInspector();
            }
        }

        private void DrawLiveDashboard()
        {
            SaveSystem activeSystem = SaveSystem.Instance ?? FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
            string resolvedPath = activeSystem != null
                ? activeSystem.FullPath
                : SaveSystem.ComputeFullPath(Application.persistentDataPath, "Saves", "save.json");

            EditorGUILayout.LabelField("Save Path", resolvedPath);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Live runtime registry is active in Play Mode.", MessageType.Info);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Edit Mode Scene Utilities", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Generate Missing Auto IDs"))
                {
                    GenerateMissingSceneIds();
                }
                if (GUILayout.Button("Check ID Conflicts"))
                {
                    StartupUtilities.CheckForIdConflicts();
                }
                EditorGUILayout.EndHorizontal();

                if (SaveablePrefabs.Instance == null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        "No SaveablePrefabs registry asset found at Assets/Resources/FlexibleSave/SaveablePrefabs.asset.",
                        MessageType.Warning);
                    if (GUILayout.Button("Create & Sync SaveablePrefabs Asset"))
                    {
                        SaveablePrefabsEditor.CreateOrSelectAsset();
                    }
                }
                return;
            }

            if (activeSystem == null)
            {
                EditorGUILayout.HelpBox("No SaveSystem component found in the active scenes.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Status",
                    $"HasLoaded: {activeSystem.HasLoaded} | IsCorrupt: {activeSystem.IsCorrupt} | IsBusy: {activeSystem.IsBusy}");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Load"))
                {
                    try { activeSystem.Load(); }
                    catch (Exception ex) { Debug.LogError($"[FlexibleSave] Load failed: {ex.Message}"); }
                }
                GUI.enabled = activeSystem.HasLoaded && !activeSystem.IsCorrupt && !activeSystem.IsBusy;
                if (GUILayout.Button("Save"))
                {
                    try { activeSystem.Save(); }
                    catch (Exception ex) { Debug.LogError($"[FlexibleSave] Save failed: {ex.Message}"); }
                }
                GUI.enabled = true;
                if (GUILayout.Button("Flush"))
                {
                    activeSystem.Flush();
                }
                if (GUILayout.Button("Delete"))
                {
                    activeSystem.Delete();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Registered Saveable Entities", EditorStyles.boldLabel);

            SaveableEntity[] snapshot = SaveableEntityRegistry.GetEntitiesSnapshot();
            m_dashboardScroll = EditorGUILayout.BeginScrollView(m_dashboardScroll);

            if (snapshot.Length == 0)
            {
                EditorGUILayout.LabelField("No saveable entities registered.");
            }
            else
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    SaveableEntity entity = snapshot[i];
                    if (entity == null) continue;
                    DrawEntityRow(entity);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntityRow(SaveableEntity entity)
        {
            if (!m_pendingIdEdits.TryGetValue(entity, out string editedId))
            {
                editedId = entity.ID ?? string.Empty;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(entity.gameObject, typeof(GameObject), allowSceneObjects: true, GUILayout.Width(170));
            EditorGUI.EndDisabledGroup();

            string updated = EditorGUILayout.TextField(editedId);
            if (!string.Equals(updated, editedId, StringComparison.Ordinal))
            {
                m_pendingIdEdits[entity] = updated;
            }

            bool hasPendingChange = !string.Equals(updated, entity.ID ?? string.Empty, StringComparison.Ordinal);
            GUI.enabled = hasPendingChange && !string.IsNullOrEmpty(updated);
            if (GUILayout.Button("Apply", GUILayout.Width(52)))
            {
                if (SaveableEntityRegistry.ChangeId(entity, updated))
                {
                    m_pendingIdEdits.Remove(entity);
                }
                else
                {
                    Debug.LogError($"[FlexibleSave] Could not apply ID '{updated}' to '{entity.name}' (empty or duplicate).");
                }
            }
            GUI.enabled = true;

            if (GUILayout.Button("New ID", GUILayout.Width(58)))
            {
                entity.GenerateID();
                m_pendingIdEdits.Remove(entity);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSaveFileInspector()
        {
            SaveSystem system = SaveSystem.Instance ?? FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
            string defaultDir = system != null
                ? Path.GetDirectoryName(system.FullPath)
                : Path.Combine(Application.persistentDataPath, "Saves");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Save File..."))
            {
                string initialDir = !string.IsNullOrEmpty(defaultDir) && Directory.Exists(defaultDir)
                    ? defaultDir
                    : Application.persistentDataPath;
                string selected = EditorUtility.OpenFilePanelWithFilters(
                    "Select Save File",
                    initialDir,
                    new[] { "Save files", "json,bak,bin,save", "All files", "*" });
                if (!string.IsNullOrEmpty(selected))
                {
                    LoadSaveFileForInspection(selected, system);
                }
            }

            if (system != null && GUILayout.Button("Inspect Current Slot"))
            {
                LoadSaveFileForInspection(system.FullPath, system);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(m_inspectedFilePath))
            {
                EditorGUILayout.LabelField("File", m_inspectedFilePath);
            }

            if (!string.IsNullOrEmpty(m_inspectorError))
            {
                EditorGUILayout.HelpBox(m_inspectorError, MessageType.Error);
            }

            if (m_inspectedRoot != null)
            {
                m_inspectorScroll = EditorGUILayout.BeginScrollView(m_inspectorScroll);
                int prevIndent = EditorGUI.indentLevel;
                try
                {
                    DrawJToken("SaveDocument", m_inspectedRoot, 0, "root");
                }
                finally
                {
                    EditorGUI.indentLevel = prevIndent;
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void LoadSaveFileForInspection(string path, SaveSystem system)
        {
            m_inspectedFilePath = path;
            m_inspectorError = null;
            m_inspectedRoot = null;

            try
            {
                if (!File.Exists(path))
                {
                    m_inspectorError = "File does not exist.";
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                var encryptor = system != null ? system.Encryptor : null;
                SaveDocument doc = SaveSystem.ParsePayload(bytes, encryptor, SaveSystem.Migrations);
                m_inspectedRoot = JObject.FromObject(doc, SaveJson.Serializer);
            }
            catch (Exception ex)
            {
                m_inspectorError = ex.Message;
            }
        }

        private void DrawJToken(string label, JToken token, int indent, string path)
        {
            EditorGUI.indentLevel = indent;

            if (token is JObject obj)
            {
                m_foldoutStates.TryGetValue(path, out bool expanded);
                expanded = EditorGUILayout.Foldout(expanded, $"{label} ({{{obj.Count}}})", toggleOnLabelClick: true);
                m_foldoutStates[path] = expanded;
                if (expanded)
                {
                    foreach (JProperty prop in obj.Properties())
                    {
                        DrawJToken(prop.Name, prop.Value, indent + 1, $"{path}.{prop.Name}");
                    }
                }
            }
            else if (token is JArray arr)
            {
                m_foldoutStates.TryGetValue(path, out bool expanded);
                expanded = EditorGUILayout.Foldout(expanded, $"{label} ([{arr.Count}])", toggleOnLabelClick: true);
                m_foldoutStates[path] = expanded;
                if (expanded)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        DrawJToken($"[{i}]", arr[i], indent + 1, $"{path}[{i}]");
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField(label, token?.ToString() ?? "null");
            }
        }

        private static void GenerateMissingSceneIds()
        {
            SaveableEntity[] entities = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                SaveableEntity entity = entities[i];
                if (entity == null || entity.IsManualId || !string.IsNullOrEmpty(entity.ID))
                {
                    continue;
                }

                Undo.RecordObject(entity, "Generate Missing SaveableEntity ID");
                entity.GenerateID();
                EditorUtility.SetDirty(entity);
                count++;
            }
            Debug.Log($"[FlexibleSave] Assigned IDs to {count} SaveableEntity objects.");
        }
    }
}
