using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FlexibleSave.Enhanced.Editor
{
    [InitializeOnLoad]
    internal static class SaveEntityDuplicateWatcher
    {
        static SaveEntityDuplicateWatcher()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (Application.isPlaying)
            {
                return;
            }

            List<SaveableEntity> createdEntities = null;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
                {
                    stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs args);
                    if (EditorUtility.EntityIdToObject(args.entityId) is GameObject go)
                    {
                        SaveableEntity[] found = go.GetComponentsInChildren<SaveableEntity>(includeInactive: true);
                        if (found != null && found.Length > 0)
                        {
                            createdEntities ??= new List<SaveableEntity>(found.Length);
                            createdEntities.AddRange(found);
                        }
                    }
                }
            }

            if (createdEntities == null || createdEntities.Count == 0)
            {
                return;
            }

            var createdSet = new HashSet<SaveableEntity>(createdEntities);
            SaveableEntity[] allEntities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < allEntities.Length; i++)
            {
                SaveableEntity entity = allEntities[i];
                if (entity == null || createdSet.Contains(entity))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entity.ID))
                {
                    existingIds.Add(entity.ID);
                }
            }

            for (int i = 0; i < createdEntities.Count; i++)
            {
                SaveableEntity created = createdEntities[i];
                if (created == null || created.IsManualId)
                {
                    continue;
                }

                string id = created.ID;
                if (!string.IsNullOrEmpty(id) && !existingIds.Add(id))
                {
                    var so = new SerializedObject(created);
                    so.Update();
                    SerializedProperty idProp = so.FindProperty("m_ID");
                    if (idProp != null)
                    {
                        Undo.RecordObject(created, "Regenerate Duplicated Entity ID");
                        string fresh = Guid.NewGuid().ToString();
                        existingIds.Add(fresh);
                        idProp.stringValue = fresh;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(created);
                    }
                }
            }
        }
    }
}
