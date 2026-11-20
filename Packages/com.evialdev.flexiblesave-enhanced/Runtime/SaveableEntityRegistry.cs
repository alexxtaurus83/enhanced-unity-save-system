using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlexibleSave.Enhanced
{
    public static partial class SaveableEntityRegistry
    {
        [AutoStaticsCleanup]
        private static Dictionary<string, SaveableEntity> s_entitiesById = new Dictionary<string, SaveableEntity>(StringComparer.Ordinal);

        [AutoStaticsCleanup]
        private static HashSet<SaveableEntity> s_allEntities = new HashSet<SaveableEntity>();

        [AutoStaticsCleanup]
        private static HashSet<string> s_pendingDeletedIds = new HashSet<string>(StringComparer.Ordinal);

        [AutoStaticsCleanup]
        private static bool s_suppressDeleteTracking;

        [AutoStaticsCleanup]
        private static bool s_isQuitting;

        // Reusable scratch buffers for main-thread save/load operations.
        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_entitySnapshot = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static Dictionary<SaveableEntity, List<SaveableEntity>> s_childrenMap = new Dictionary<SaveableEntity, List<SaveableEntity>>();

        [AutoStaticsCleanup]
        private static Stack<List<SaveableEntity>> s_listPool = new Stack<List<SaveableEntity>>();

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_rootEntities = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static List<string> s_idsToDeleteBuffer = new List<string>();

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_childLookupBuffer = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static HashSet<string> s_deletedSetBuffer = new HashSet<string>(StringComparer.Ordinal);

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_loadedEntitiesBuffer = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_loadedPhaseBuffer = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_sceneScanBuffer = new List<SaveableEntity>();

        [AutoStaticsCleanup]
        private static List<SaveableEntity> s_detachBuffer = new List<SaveableEntity>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeHooks()
        {
            s_isQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                s_sceneScanBuffer.Clear();
                root.GetComponentsInChildren(includeInactive: true, s_sceneScanBuffer);
                for (int j = 0; j < s_sceneScanBuffer.Count; j++)
                {
                    SaveableEntity entity = s_sceneScanBuffer[j];
                    if (entity != null &&
                        (entity.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0 &&
                        !s_allEntities.Contains(entity))
                    {
                        TryRegister(entity);
                    }
                }
            }
            s_sceneScanBuffer.Clear();
        }

        private static void OnApplicationQuitting()
        {
            s_isQuitting = true;
        }

        public static int Count => s_entitiesById.Count;

        public static void Clear()
        {
            s_entitiesById.Clear();
            s_allEntities.Clear();
            s_pendingDeletedIds.Clear();
            s_suppressDeleteTracking = false;
            s_entitySnapshot.Clear();
            ReturnChildrenMapLists();
            s_rootEntities.Clear();
            s_idsToDeleteBuffer.Clear();
            s_childLookupBuffer.Clear();
            s_deletedSetBuffer.Clear();
            s_loadedEntitiesBuffer.Clear();
            s_loadedPhaseBuffer.Clear();
            s_sceneScanBuffer.Clear();
            s_detachBuffer.Clear();
            IDFinder.ClearCache();
        }

        internal static void ClearPendingDeletedIds()
        {
            s_pendingDeletedIds.Clear();
        }

        public static SaveableEntity GetEntity(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (s_entitiesById.TryGetValue(id, out SaveableEntity entity) && entity != null)
            {
                return entity;
            }

            return null;
        }

        public static bool ContainsId(string id)
        {
            return !string.IsNullOrEmpty(id) && s_entitiesById.TryGetValue(id, out SaveableEntity existing) && existing != null;
        }

        public static SaveableEntity[] GetEntitiesSnapshot()
        {
            PruneNullEntities();
            var result = new SaveableEntity[s_allEntities.Count];
            s_allEntities.CopyTo(result);
            return result;
        }

        internal static IReadOnlyCollection<string> PendingDeletedIds => s_pendingDeletedIds;

        public static bool TryRegister(SaveableEntity entity)
        {
            if (entity == null)
            {
                return false;
            }

            string id = entity.ID;
            if (string.IsNullOrEmpty(id))
            {
                if (entity.IsManualId)
                {
                    Debug.LogError($"[FlexibleSave] SaveableEntity '{entity.name}' has Manual ID enabled but its ID is empty. Entity was not registered.", entity);
                    return false;
                }

                id = Guid.NewGuid().ToString();
                entity.AssignInternalId(id);
            }

            if (s_entitiesById.TryGetValue(id, out SaveableEntity existing))
            {
                if (existing == entity)
                {
                    s_allEntities.Add(entity);
                    return true;
                }

                if (existing != null)
                {
                    if (entity.IsManualId)
                    {
                        Debug.LogError($"[FlexibleSave] Duplicate manual ID '{id}' on '{entity.name}' (already registered by '{existing.name}'). Duplicate entity was not registered.", entity);
                        return false;
                    }

                    // Clone detected for an auto-ID entity: assign a fresh GUID to the clone only.
                    string newId;
                    do
                    {
                        newId = Guid.NewGuid().ToString();
                    } while (s_entitiesById.TryGetValue(newId, out SaveableEntity collision) && collision != null);

                    entity.AssignInternalId(newId);
                    id = newId;
                }
                else
                {
                    // Stale destroyed entry
                    s_entitiesById.Remove(id);
                }
            }

            s_entitiesById[id] = entity;
            s_allEntities.Add(entity);
            return true;
        }

        public static bool ChangeId(SaveableEntity entity, string newId)
        {
            if (entity == null || string.IsNullOrEmpty(newId))
            {
                return false;
            }

            string oldId = entity.ID;
            if (string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                if (!s_entitiesById.TryGetValue(newId, out SaveableEntity current) || current == null)
                {
                    s_entitiesById[newId] = entity;
                    s_allEntities.Add(entity);
                }
                return s_entitiesById.TryGetValue(newId, out SaveableEntity owner) && owner == entity;
            }

            if (s_entitiesById.TryGetValue(newId, out SaveableEntity occupant) && occupant != null && occupant != entity)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(oldId) &&
                s_entitiesById.TryGetValue(oldId, out SaveableEntity oldOwner) &&
                oldOwner == entity)
            {
                s_entitiesById.Remove(oldId);
            }

            entity.AssignInternalId(newId);
            s_entitiesById[newId] = entity;
            s_allEntities.Add(entity);
            return true;
        }

        internal static void OnEntityDestroyed(SaveableEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            s_allEntities.Remove(entity);

            string id = entity.ID;
            if (!string.IsNullOrEmpty(id))
            {
                if (s_entitiesById.TryGetValue(id, out SaveableEntity owner) && owner == entity)
                {
                    s_entitiesById.Remove(id);

                    if (!s_suppressDeleteTracking && !s_isQuitting && entity.gameObject.scene.isLoaded)
                    {
                        s_pendingDeletedIds.Add(id);
                    }
                }
            }
        }

        internal static void CaptureState(SaveDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (document.entities == null)
            {
                document.entities = new Dictionary<string, EntityRecord>(StringComparer.Ordinal);
            }

            if (document.deleted == null)
            {
                document.deleted = new List<string>();
            }

            // Build deleted set combining existing tombstones + newly destroyed entities.
            s_deletedSetBuffer.Clear();
            for (int i = 0; i < document.deleted.Count; i++)
            {
                string existingDeleted = document.deleted[i];
                if (!string.IsNullOrEmpty(existingDeleted))
                {
                    s_deletedSetBuffer.Add(existingDeleted);
                }
            }

            foreach (string deletedId in s_pendingDeletedIds)
            {
                if (!string.IsNullOrEmpty(deletedId))
                {
                    document.entities.Remove(deletedId);
                    s_deletedSetBuffer.Add(deletedId);
                }
            }
            s_pendingDeletedIds.Clear();

            // Snapshot live entities and prune destroyed entries.
            PruneAndSnapshotEntities(s_entitySnapshot);

            // Build parent -> direct child SaveableEntity map in a single O(N * depth) pass.
            ReturnChildrenMapLists();
            s_rootEntities.Clear();

            for (int i = 0; i < s_entitySnapshot.Count; i++)
            {
                SaveableEntity entity = s_entitySnapshot[i];
                if (entity == null)
                {
                    continue;
                }

                entity.SetParentEntityInternal(null);
            }

            for (int i = 0; i < s_entitySnapshot.Count; i++)
            {
                SaveableEntity entity = s_entitySnapshot[i];
                if (entity == null)
                {
                    continue;
                }

                SaveableEntity parentEntity = FindNearestRegisteredParentEntity(entity);
                if (parentEntity != null)
                {
                    entity.SetParentEntityInternal(parentEntity);
                    if (!s_childrenMap.TryGetValue(parentEntity, out List<SaveableEntity> childList))
                    {
                        childList = RentList();
                        s_childrenMap[parentEntity] = childList;
                    }
                    childList.Add(entity);
                }
                else
                {
                    s_rootEntities.Add(entity);
                }
            }

            // Capture root entities (which recursively capture their children).
            for (int i = 0; i < s_rootEntities.Count; i++)
            {
                SaveableEntity root = s_rootEntities[i];
                if (root == null || string.IsNullOrEmpty(root.ID))
                {
                    continue;
                }

                document.entities.TryGetValue(root.ID, out EntityRecord existingRecord);
                EntityRecord record = root.CaptureRecord(s_childrenMap, existingRecord);
                if (record != null)
                {
                    document.entities[root.ID] = record;
                    s_deletedSetBuffer.Remove(root.ID);
                }
                else
                {
                    document.entities.Remove(root.ID);
                }
            }

            // Remove stale root entries for entities now parented under another SaveableEntity,
            // and ensure no live entity ID remains marked deleted.
            for (int i = 0; i < s_entitySnapshot.Count; i++)
            {
                SaveableEntity entity = s_entitySnapshot[i];
                if (entity != null && !string.IsNullOrEmpty(entity.ID))
                {
                    if (entity.ParentEntity != null)
                    {
                        document.entities.Remove(entity.ID);
                    }
                    s_deletedSetBuffer.Remove(entity.ID);
                }
            }

            document.deleted.Clear();
            foreach (string id in s_deletedSetBuffer)
            {
                document.deleted.Add(id);
            }
            s_deletedSetBuffer.Clear();

            ReturnChildrenMapLists();
            s_entitySnapshot.Clear();
            s_rootEntities.Clear();
        }

        internal static void RegisterUnregisteredSceneEntities()
        {
            SaveableEntity[] sceneEntities = UnityEngine.Object.FindObjectsByType<SaveableEntity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < sceneEntities.Length; i++)
            {
                SaveableEntity entity = sceneEntities[i];
                if (entity != null &&
                    entity.gameObject.scene.IsValid() &&
                    (entity.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0 &&
                    !s_allEntities.Contains(entity))
                {
                    TryRegister(entity);
                }
            }
        }

        internal static void RestoreFromDocument(
            SaveDocument document,
            IProgress<float> progress,
            out int restored,
            out int skipped)
        {
            restored = 0;
            skipped = 0;

            if (document == null)
            {
                return;
            }

            s_pendingDeletedIds.Clear();

            s_suppressDeleteTracking = true;
            try
            {
                // Ensure even never-activated inactive scene entities are registered before load.
                RegisterUnregisteredSceneEntities();

                // Destroy ONLY explicitly recorded deleted IDs; leave scene entities missing from save intact.
                if (document.deleted != null && document.deleted.Count > 0)
                {
                    s_idsToDeleteBuffer.Clear();
                    for (int i = 0; i < document.deleted.Count; i++)
                    {
                        string id = document.deleted[i];
                        if (!string.IsNullOrEmpty(id))
                        {
                            s_idsToDeleteBuffer.Add(id);
                        }
                    }

                    for (int i = 0; i < s_idsToDeleteBuffer.Count; i++)
                    {
                        SaveableEntity target = GetEntity(s_idsToDeleteBuffer[i]);
                        if (target != null)
                        {
                            s_allEntities.Remove(target);
                            s_entitiesById.Remove(s_idsToDeleteBuffer[i]);
                            UnityEngine.Object.DestroyImmediate(target.gameObject);
                        }
                    }
                    s_idsToDeleteBuffer.Clear();
                }

                // Prepare all live entities for load (clears m_parent and component state cache).
                PruneAndSnapshotEntities(s_entitySnapshot);
                for (int i = 0; i < s_entitySnapshot.Count; i++)
                {
                    SaveableEntity entity = s_entitySnapshot[i];
                    if (entity != null)
                    {
                        entity.PrepareForLoad();
                    }
                }

                // Restore entities from document.
                s_loadedEntitiesBuffer.Clear();
                if (document.entities != null && document.entities.Count > 0)
                {
                    int total = document.entities.Count;
                    int processed = 0;

                    foreach (KeyValuePair<string, EntityRecord> kvp in document.entities)
                    {
                        EntityRecord record = kvp.Value;
                        if (record == null || record.meta == null || string.IsNullOrEmpty(record.meta.id))
                        {
                            skipped++;
                        }
                        else
                        {
                            SaveableEntity restoredEntity = RestoreEntityRecord(
                                record,
                                parent: null,
                                s_loadedEntitiesBuffer,
                                parentReinstantiated: false);
                            if (restoredEntity != null)
                            {
                                restored++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }

                        processed++;
                        progress?.Report(0.1f + 0.7f * ((float)processed / total));
                    }
                }
                else
                {
                    progress?.Report(0.8f);
                }

                s_loadedPhaseBuffer.Clear();
                s_loadedPhaseBuffer.AddRange(s_loadedEntitiesBuffer);
                s_loadedEntitiesBuffer.Clear();

                // Refresh scene object lookup cache before Phase 2 cross-object reference resolution.
                IDFinder.ClearSceneCache();

                // Phase 2: AllObjectsStateLoaded (snapshot iteration, null-safe).
                for (int i = 0; i < s_loadedPhaseBuffer.Count; i++)
                {
                    SaveableEntity entity = s_loadedPhaseBuffer[i];
                    if (entity != null)
                    {
                        entity.NotifyAllObjectsStateLoaded();
                    }
                }
                progress?.Report(0.9f);

                // Phase 3: AllObjectsDependenciesRestored (snapshot iteration, null-safe).
                for (int i = 0; i < s_loadedPhaseBuffer.Count; i++)
                {
                    SaveableEntity entity = s_loadedPhaseBuffer[i];
                    if (entity != null)
                    {
                        entity.NotifyAllObjectsDependenciesRestored();
                    }
                }
                progress?.Report(1f);
            }
            finally
            {
                s_suppressDeleteTracking = false;
                s_entitySnapshot.Clear();
                s_loadedEntitiesBuffer.Clear();
                s_loadedPhaseBuffer.Clear();
            }
        }

        private static SaveableEntity RestoreEntityRecord(
            EntityRecord record,
            SaveableEntity parent,
            List<SaveableEntity> loadedEntities,
            bool parentReinstantiated)
        {
            EntityMeta meta = record.meta;
            if (meta == null || string.IsNullOrEmpty(meta.id))
            {
                return null;
            }

            SaveableEntity instance = null;
            bool thisReinstantiated = false;

            if (meta.needsReinstantiation && meta.hasPrefab)
            {
                if (parent != null && !string.IsNullOrEmpty(meta.childId))
                {
                    parent.DestroyChild(meta.childId);
                }

                instance = ReinstantiateFromMeta(meta);
                thisReinstantiated = instance != null;
            }
            else if (!string.IsNullOrEmpty(meta.childId) && parent != null)
            {
                ObjectFinder.GetFirstChildLayerOfType(parent.gameObject, s_childLookupBuffer);
                for (int i = 0; i < s_childLookupBuffer.Count; i++)
                {
                    SaveableEntity child = s_childLookupBuffer[i];
                    if (child != null && string.Equals(child.ChildID, meta.childId, StringComparison.Ordinal))
                    {
                        instance = child;
                        break;
                    }
                }
                s_childLookupBuffer.Clear();

                if (instance != null)
                {
                    instance.RecordInitialChildIds();
                    if (parentReinstantiated)
                    {
                        ApplyDeletedChildren(meta, instance);
                    }
                }
                else if (SaveSystem.VerboseLogging)
                {
                    Debug.LogWarning($"[FlexibleSave] Child with childId '{meta.childId}' not found under parent '{parent.name}'.", parent);
                }
            }
            else
            {
                instance = GetEntity(meta.id);
                if (instance == null && meta.hasPrefab)
                {
                    instance = ReinstantiateFromMeta(meta);
                    thisReinstantiated = instance != null;
                }
            }

            if (instance == null)
            {
                if (SaveSystem.VerboseLogging)
                {
                    Debug.LogWarning($"[FlexibleSave] Failed to find or instantiate entity '{meta.name}' (ID: '{meta.id}').");
                }
                return null;
            }

            instance.ApplyLoadedRecord(record, parent);
            loadedEntities.Add(instance);

            if (record.children != null)
            {
                for (int i = 0; i < record.children.Count; i++)
                {
                    EntityRecord childRecord = record.children[i];
                    if (childRecord != null)
                    {
                        RestoreEntityRecord(childRecord, instance, loadedEntities, thisReinstantiated);
                    }
                }
            }

            return instance;
        }

        private static SaveableEntity ReinstantiateFromMeta(EntityMeta meta)
        {
            SaveableEntity existing = GetEntity(meta.id);
            if (existing != null)
            {
                DetachNonPrefabChildren(existing);
                s_allEntities.Remove(existing);
                s_entitiesById.Remove(meta.id);
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            GameObject prefab = SaveablePrefabs.GetPrefab(meta.prefabId);
            if (prefab == null)
            {
                Debug.LogWarning($"[FlexibleSave] Cannot reinstantiate '{meta.name}': prefab '{meta.prefabId}' not found in SaveablePrefabs.");
                return null;
            }

            GameObject newObj = UnityEngine.Object.Instantiate(prefab);
            if (!newObj.TryGetComponent<SaveableEntity>(out SaveableEntity newEntity))
            {
                Debug.LogWarning($"[FlexibleSave] Instantiated prefab '{prefab.name}' does not have a SaveableEntity component.", newObj);
                UnityEngine.Object.DestroyImmediate(newObj);
                return null;
            }

            SetupInstantiated(meta, newEntity);
            return newEntity;
        }

        private static void DetachNonPrefabChildren(SaveableEntity existing)
        {
            if (existing == null)
            {
                return;
            }

            s_detachBuffer.Clear();
            CollectNonPrefabChildrenToDetach(existing.transform, s_detachBuffer);
            for (int i = 0; i < s_detachBuffer.Count; i++)
            {
                SaveableEntity child = s_detachBuffer[i];
                if (child != null)
                {
                    child.transform.SetParent(null, worldPositionStays: false);
                }
            }
            s_detachBuffer.Clear();
        }

        private static void CollectNonPrefabChildrenToDetach(Transform current, List<SaveableEntity> toDetach)
        {
            for (int i = 0; i < current.childCount; i++)
            {
                Transform childTransform = current.GetChild(i);
                if (childTransform.TryGetComponent<SaveableEntity>(out SaveableEntity childEntity) &&
                    string.IsNullOrEmpty(childEntity.ChildID))
                {
                    toDetach.Add(childEntity);
                    continue;
                }

                CollectNonPrefabChildrenToDetach(childTransform, toDetach);
            }
        }

        private static void SetupInstantiated(EntityMeta meta, SaveableEntity newEntity)
        {
            // Ensure ID matches the saved entity ID (overriding any temp GUID generated during Awake).
            ChangeId(newEntity, meta.id);

            // Capture initial prefab child IDs before removing deleted children so future saves keep track of them.
            newEntity.RecordInitialChildIds();
            ApplyDeletedChildren(meta, newEntity);
        }

        private static void ApplyDeletedChildren(EntityMeta meta, SaveableEntity target)
        {
            if (meta.deletedChildren != null && meta.deletedChildren.Count > 0)
            {
                ObjectFinder.GetFirstChildLayerOfType(target.gameObject, s_childLookupBuffer);
                for (int i = 0; i < meta.deletedChildren.Count; i++)
                {
                    target.DestroyChild(meta.deletedChildren[i], s_childLookupBuffer);
                }
                s_childLookupBuffer.Clear();
            }
        }

        private static SaveableEntity FindNearestRegisteredParentEntity(SaveableEntity entity)
        {
            Transform current = entity.transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<SaveableEntity>(out SaveableEntity candidate) && s_allEntities.Contains(candidate))
                {
                    return candidate;
                }
                current = current.parent;
            }
            return null;
        }

        private static void PruneNullEntities()
        {
            s_allEntities.RemoveWhere(e => e == null);
        }

        private static void PruneAndSnapshotEntities(List<SaveableEntity> snapshot)
        {
            snapshot.Clear();
            s_allEntities.RemoveWhere(e => e == null);
            foreach (SaveableEntity entity in s_allEntities)
            {
                snapshot.Add(entity);
            }
        }

        private static List<SaveableEntity> RentList()
        {
            if (s_listPool.Count > 0)
            {
                List<SaveableEntity> list = s_listPool.Pop();
                list.Clear();
                return list;
            }
            return new List<SaveableEntity>(4);
        }

        private static void ReturnChildrenMapLists()
        {
            foreach (KeyValuePair<SaveableEntity, List<SaveableEntity>> kvp in s_childrenMap)
            {
                kvp.Value.Clear();
                s_listPool.Push(kvp.Value);
            }
            s_childrenMap.Clear();
        }
    }
}
