using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlexibleSave.Enhanced
{
    [DisallowMultipleComponent]
    public partial class SaveableEntity : MonoBehaviour, ISaveID
    {
        [SerializeField] private string m_ID;
        [SerializeField] private bool m_manualId;
        [SerializeField] private string m_childID;
        [SerializeField] private string m_prefabID;
        [SerializeField] private bool m_saveTransform;

        private SaveableEntity m_parent;
        private Dictionary<string, JToken> m_componentStates;
        private EntityMeta m_loadedMeta;
        private readonly List<string> m_knownChildIds = new List<string>();
        private bool m_initialChildIdsRecorded;

        // Reusable scratch buffers (main-thread only) to avoid per-entity allocations during save/load.
        private static readonly List<ISaveable> s_saveableBuffer = new List<ISaveable>(8);
        private static readonly List<ISaveable> s_saveableSnapshotBuffer = new List<ISaveable>(8);
        private static bool s_saveableSnapshotInUse;
        private static readonly List<SaveableEntity> s_childBuffer = new List<SaveableEntity>(8);
        private static readonly List<Transform> s_pathSegments = new List<Transform>(8);
        private static readonly StringBuilder s_pathBuilder = new StringBuilder(128);

        public string ID => m_ID;
        internal SaveableEntity ParentEntity => m_parent;
        public bool IsManualId
        {
            get => m_manualId;
            set => m_manualId = value;
        }
        public string ChildID => m_childID;
        public string PrefabID => m_prefabID;
        public bool SaveTransform
        {
            get => m_saveTransform;
            set => m_saveTransform = value;
        }

        public string GetID() => m_ID;

        public void SetID(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (Application.isPlaying)
            {
                SaveableEntityRegistry.ChangeId(this, id);
            }
            else
            {
                m_ID = id;
            }
        }

        internal void AssignInternalId(string id)
        {
            m_ID = id;
        }

        internal void SetParentEntityInternal(SaveableEntity parent)
        {
            m_parent = parent;
        }

        public string GetPrefabID() => m_prefabID;
        public void SetSaveTransform(bool enable) => m_saveTransform = enable;
        public bool GetSaveTransform() => m_saveTransform;

        public static SaveableEntity FindID(string id) => SaveableEntityRegistry.GetEntity(id);

        private void Reset()
        {
            m_saveTransform = !string.IsNullOrEmpty(m_prefabID);
        }

        private void Awake()
        {
            RecordInitialChildIds();
            SaveableEntityRegistry.TryRegister(this);
        }

        private void OnDestroy()
        {
            SaveableEntityRegistry.OnEntityDestroyed(this);
        }

        internal void RecordInitialChildIds()
        {
            if (m_initialChildIdsRecorded)
            {
                return;
            }

            m_initialChildIdsRecorded = true;
            m_knownChildIds.Clear();

            if (string.IsNullOrEmpty(m_prefabID) && string.IsNullOrEmpty(m_childID))
            {
                return;
            }

            ObjectFinder.GetFirstChildLayerOfType(gameObject, s_childBuffer);
            for (int i = 0; i < s_childBuffer.Count; i++)
            {
                SaveableEntity child = s_childBuffer[i];
                if (child != null && !string.IsNullOrEmpty(child.m_childID))
                {
                    m_knownChildIds.Add(child.m_childID);
                }
            }
            s_childBuffer.Clear();
        }

        internal void PrepareForLoad()
        {
            m_parent = null;
            m_componentStates = null;
            m_loadedMeta = null;
        }

        [ContextMenu("Generate ID")]
        public void GenerateID()
        {
            string newId = Guid.NewGuid().ToString();
            if (Application.isPlaying)
            {
                SaveableEntityRegistry.ChangeId(this, newId);
            }
            else
            {
                m_ID = newId;
            }
        }

        public void ResetTransform()
        {
            if (m_loadedMeta != null && m_loadedMeta.transform.HasValue)
            {
                TransformData td = m_loadedMeta.transform.Value;
                transform.localPosition = td.local.position.ToVector3();
                transform.localRotation = td.local.rotation.ToQuaternion();
                transform.localScale = td.local.scale.ToVector3();
            }
        }

        internal EntityRecord CaptureRecord(
            Dictionary<SaveableEntity, List<SaveableEntity>> childrenMap,
            EntityRecord previousRecord = null)
        {
            GetComponents(s_saveableBuffer);
            int saveableCount = s_saveableBuffer.Count;
            bool anyComponentChecked = saveableCount > 0;
            bool anyComponentSaved = false;
            bool hadSaveError = false;
            bool needsReinstantiation = false;

            Dictionary<string, JToken> componentStates = null;

            if (saveableCount == 1)
            {
                ISaveable single = s_saveableBuffer[0];
                s_saveableBuffer.Clear();
                if (single != null && single.NeedsToBeSaved())
                {
                    CaptureSingleComponent(
                        single,
                        previousRecord,
                        ref componentStates,
                        ref anyComponentSaved,
                        ref hadSaveError,
                        ref needsReinstantiation);
                }
            }
            else if (saveableCount > 1)
            {
                if (!s_saveableSnapshotInUse)
                {
                    s_saveableSnapshotInUse = true;
                    s_saveableSnapshotBuffer.Clear();
                    s_saveableSnapshotBuffer.AddRange(s_saveableBuffer);
                    s_saveableBuffer.Clear();
                    try
                    {
                        for (int i = 0; i < s_saveableSnapshotBuffer.Count; i++)
                        {
                            ISaveable saveable = s_saveableSnapshotBuffer[i];
                            if (saveable != null && saveable.NeedsToBeSaved())
                            {
                                CaptureSingleComponent(
                                    saveable,
                                    previousRecord,
                                    ref componentStates,
                                    ref anyComponentSaved,
                                    ref hadSaveError,
                                    ref needsReinstantiation);
                            }
                        }
                    }
                    finally
                    {
                        s_saveableSnapshotBuffer.Clear();
                        s_saveableSnapshotInUse = false;
                    }
                }
                else
                {
                    ISaveable[] saveablesSnapshot = s_saveableBuffer.ToArray();
                    s_saveableBuffer.Clear();
                    for (int i = 0; i < saveablesSnapshot.Length; i++)
                    {
                        ISaveable saveable = saveablesSnapshot[i];
                        if (saveable != null && saveable.NeedsToBeSaved())
                        {
                            CaptureSingleComponent(
                                saveable,
                                previousRecord,
                                ref componentStates,
                                ref anyComponentSaved,
                                ref hadSaveError,
                                ref needsReinstantiation);
                        }
                    }
                }
            }
            else
            {
                s_saveableBuffer.Clear();
            }

            childrenMap.TryGetValue(this, out List<SaveableEntity> directChildren);

            List<EntityRecord> childRecords = null;
            if (directChildren != null && directChildren.Count > 0)
            {
                for (int i = 0; i < directChildren.Count; i++)
                {
                    SaveableEntity child = directChildren[i];
                    if (child == null)
                    {
                        continue;
                    }

                    EntityRecord prevChildRecord = FindPreviousChildRecord(previousRecord, child);
                    EntityRecord childRecord = child.CaptureRecord(childrenMap, prevChildRecord);
                    if (childRecord != null)
                    {
                        childRecords ??= new List<EntityRecord>(directChildren.Count);
                        childRecords.Add(childRecord);
                    }
                }
            }

            if (anyComponentChecked && !anyComponentSaved && (childRecords == null || childRecords.Count == 0))
            {
                return hadSaveError ? previousRecord : null;
            }

            string parentId = null;
            string parentPath = null;
            if (m_parent != null)
            {
                parentId = m_parent.m_ID;
                parentPath = ComputeRelativeParentPath(m_parent.transform, transform.parent);
            }

            TransformData? transformData = null;
            if (m_saveTransform)
            {
                transformData = new TransformData
                {
                    local = new LocRotScale
                    {
                        position = new Vector3Data(transform.localPosition),
                        rotation = new Vector4Data(transform.localRotation),
                        scale = new Vector3Data(transform.localScale)
                    }
                };
            }

            List<string> deletedChildren = ComputeDeletedChildren(directChildren);
            bool hasPrefab = !string.IsNullOrEmpty(m_prefabID);

            var meta = new EntityMeta
            {
                name = gameObject.name,
                id = m_ID,
                parentId = parentId,
                parentPath = parentPath,
                hasPrefab = hasPrefab,
                prefabId = hasPrefab ? m_prefabID : null,
                childId = string.IsNullOrEmpty(m_childID) ? null : m_childID,
                saveTransform = m_saveTransform,
                transform = transformData,
                deletedChildren = deletedChildren,
                needsReinstantiation = needsReinstantiation
            };

            return new EntityRecord
            {
                meta = meta,
                components = componentStates,
                children = childRecords
            };
        }

        private static EntityRecord FindPreviousChildRecord(EntityRecord previousParentRecord, SaveableEntity child)
        {
            if (previousParentRecord?.children == null || child == null)
            {
                return null;
            }

            List<EntityRecord> prevChildren = previousParentRecord.children;
            string childEntityId = child.ID;
            if (!string.IsNullOrEmpty(childEntityId))
            {
                for (int i = 0; i < prevChildren.Count; i++)
                {
                    EntityRecord candidate = prevChildren[i];
                    if (candidate?.meta != null && string.Equals(candidate.meta.id, childEntityId, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }

            string childSubId = child.ChildID;
            if (!string.IsNullOrEmpty(childSubId))
            {
                for (int i = 0; i < prevChildren.Count; i++)
                {
                    EntityRecord candidate = prevChildren[i];
                    if (candidate?.meta != null && string.Equals(candidate.meta.childId, childSubId, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private void CaptureSingleComponent(
            ISaveable saveable,
            EntityRecord previousRecord,
            ref Dictionary<string, JToken> componentStates,
            ref bool anyComponentSaved,
            ref bool hadSaveError,
            ref bool needsReinstantiation)
        {
            string key = GetComponentKey(saveable);
            try
            {
                object state = saveable.SaveState();
                componentStates ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
                if (componentStates.ContainsKey(key))
                {
                    Debug.LogWarning(
                        $"[FlexibleSave] Duplicate SaveKey '{key}' on GameObject '{gameObject.name}' (component '{saveable.GetType().FullName}'). Override ISaveable.SaveKey to make each component key unique.",
                        gameObject);
                }
                componentStates[key] = SaveJson.ToToken(state);
                anyComponentSaved = true;
                needsReinstantiation |= saveable.NeedsReinstantiation();
            }
            catch (NotImplementedException)
            {
                hadSaveError = true;
                PreservePreviousComponentState(previousRecord, key, ref componentStates, ref anyComponentSaved, ref needsReinstantiation);
                Debug.LogError(
                    $"[FlexibleSave] SaveState is not implemented on '{saveable.GetType().FullName}' (GameObject '{gameObject.name}').",
                    gameObject);
            }
            catch (Exception ex)
            {
                hadSaveError = true;
                PreservePreviousComponentState(previousRecord, key, ref componentStates, ref anyComponentSaved, ref needsReinstantiation);
                Debug.LogError(
                    $"[FlexibleSave] Error in SaveState on '{saveable.GetType().FullName}' (GameObject '{gameObject.name}'): {ex}",
                    gameObject);
            }
        }

        private static void PreservePreviousComponentState(
            EntityRecord previousRecord,
            string key,
            ref Dictionary<string, JToken> componentStates,
            ref bool anyComponentSaved,
            ref bool needsReinstantiation)
        {
            if (previousRecord?.components != null && previousRecord.components.TryGetValue(key, out JToken prevToken))
            {
                componentStates ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
                componentStates[key] = prevToken;
                anyComponentSaved = true;
                if (previousRecord.meta != null)
                {
                    needsReinstantiation |= previousRecord.meta.needsReinstantiation;
                }
            }
        }

        internal void ApplyLoadedRecord(EntityRecord record, SaveableEntity parent)
        {
            EntityMeta meta = record.meta;
            m_loadedMeta = meta;
            m_componentStates = record.components;

            if (!string.IsNullOrEmpty(meta.name))
            {
                gameObject.name = meta.name;
            }

            if (!string.IsNullOrEmpty(meta.id) && !string.Equals(m_ID, meta.id, StringComparison.Ordinal))
            {
                SaveableEntityRegistry.ChangeId(this, meta.id);
            }

            if (!string.IsNullOrEmpty(meta.prefabId))
            {
                m_prefabID = meta.prefabId;
            }

            if (!string.IsNullOrEmpty(meta.childId))
            {
                m_childID = meta.childId;
            }

            m_saveTransform = meta.saveTransform;

            // Parent first with worldPositionStays: false, then apply saved local transform.
            if (parent != null)
            {
                AttachToParent(parent, meta.parentPath);
            }
            else if (HasSaveableEntityAncestor(transform.parent))
            {
                transform.SetParent(null, worldPositionStays: false);
                m_parent = null;
            }

            if (m_saveTransform)
            {
                ResetTransform();
            }

            InvokeLoadState();
        }

        private static bool HasSaveableEntityAncestor(Transform current)
        {
            while (current != null)
            {
                if (current.TryGetComponent<SaveableEntity>(out _))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        public void LoadState(object state)
        {
            if (state is Dictionary<string, JToken> dict)
            {
                m_componentStates = dict;
            }
            else if (state is JObject obj)
            {
                m_componentStates = obj.ToObject<Dictionary<string, JToken>>(SaveJson.Serializer);
            }
            else
            {
                m_componentStates = null;
            }

            InvokeLoadState();
        }

        internal void InvokeLoadState()
        {
            if (m_componentStates == null)
            {
                return;
            }

            GetComponents(s_saveableBuffer);
            int count = s_saveableBuffer.Count;
            if (count == 1)
            {
                ISaveable single = s_saveableBuffer[0];
                s_saveableBuffer.Clear();
                if (single != null && m_componentStates.TryGetValue(GetComponentKey(single), out JToken token))
                {
                    single.LoadState(token);
                }
                return;
            }

            if (count == 0)
            {
                return;
            }

            if (!s_saveableSnapshotInUse)
            {
                s_saveableSnapshotInUse = true;
                s_saveableSnapshotBuffer.Clear();
                s_saveableSnapshotBuffer.AddRange(s_saveableBuffer);
                s_saveableBuffer.Clear();
                try
                {
                    for (int i = 0; i < s_saveableSnapshotBuffer.Count; i++)
                    {
                        ISaveable saveable = s_saveableSnapshotBuffer[i];
                        if (saveable == null)
                        {
                            continue;
                        }

                        string key = GetComponentKey(saveable);
                        if (m_componentStates.TryGetValue(key, out JToken token))
                        {
                            saveable.LoadState(token);
                        }
                    }
                }
                finally
                {
                    s_saveableSnapshotBuffer.Clear();
                    s_saveableSnapshotInUse = false;
                }
            }
            else
            {
                ISaveable[] saveablesSnapshot = s_saveableBuffer.ToArray();
                s_saveableBuffer.Clear();
                for (int i = 0; i < saveablesSnapshot.Length; i++)
                {
                    ISaveable saveable = saveablesSnapshot[i];
                    if (saveable == null)
                    {
                        continue;
                    }

                    string key = GetComponentKey(saveable);
                    if (m_componentStates.TryGetValue(key, out JToken token))
                    {
                        saveable.LoadState(token);
                    }
                }
            }
        }

        internal void NotifyAllObjectsStateLoaded()
        {
            if (m_componentStates == null)
            {
                return;
            }

            GetComponents(s_saveableBuffer);
            int count = s_saveableBuffer.Count;
            if (count == 1)
            {
                ISaveable single = s_saveableBuffer[0];
                s_saveableBuffer.Clear();
                if (single != null && m_componentStates.TryGetValue(GetComponentKey(single), out JToken token))
                {
                    single.AllObjectsStateLoaded(token);
                }
                return;
            }

            if (count == 0)
            {
                return;
            }

            if (!s_saveableSnapshotInUse)
            {
                s_saveableSnapshotInUse = true;
                s_saveableSnapshotBuffer.Clear();
                s_saveableSnapshotBuffer.AddRange(s_saveableBuffer);
                s_saveableBuffer.Clear();
                try
                {
                    for (int i = 0; i < s_saveableSnapshotBuffer.Count; i++)
                    {
                        ISaveable saveable = s_saveableSnapshotBuffer[i];
                        if (saveable == null)
                        {
                            continue;
                        }

                        string key = GetComponentKey(saveable);
                        if (m_componentStates.TryGetValue(key, out JToken token))
                        {
                            saveable.AllObjectsStateLoaded(token);
                        }
                    }
                }
                finally
                {
                    s_saveableSnapshotBuffer.Clear();
                    s_saveableSnapshotInUse = false;
                }
            }
            else
            {
                ISaveable[] saveablesSnapshot = s_saveableBuffer.ToArray();
                s_saveableBuffer.Clear();
                for (int i = 0; i < saveablesSnapshot.Length; i++)
                {
                    ISaveable saveable = saveablesSnapshot[i];
                    if (saveable == null)
                    {
                        continue;
                    }

                    string key = GetComponentKey(saveable);
                    if (m_componentStates.TryGetValue(key, out JToken token))
                    {
                        saveable.AllObjectsStateLoaded(token);
                    }
                }
            }
        }

        internal void NotifyAllObjectsDependenciesRestored()
        {
            if (m_loadedMeta == null)
            {
                return;
            }

            GetComponents(s_saveableBuffer);
            int count = s_saveableBuffer.Count;
            if (count == 1)
            {
                ISaveable single = s_saveableBuffer[0];
                s_saveableBuffer.Clear();
                single?.AllObjectsDependenciesRestored();
                return;
            }

            if (count == 0)
            {
                return;
            }

            if (!s_saveableSnapshotInUse)
            {
                s_saveableSnapshotInUse = true;
                s_saveableSnapshotBuffer.Clear();
                s_saveableSnapshotBuffer.AddRange(s_saveableBuffer);
                s_saveableBuffer.Clear();
                try
                {
                    for (int i = 0; i < s_saveableSnapshotBuffer.Count; i++)
                    {
                        s_saveableSnapshotBuffer[i]?.AllObjectsDependenciesRestored();
                    }
                }
                finally
                {
                    s_saveableSnapshotBuffer.Clear();
                    s_saveableSnapshotInUse = false;
                }
            }
            else
            {
                ISaveable[] saveablesSnapshot = s_saveableBuffer.ToArray();
                s_saveableBuffer.Clear();
                for (int i = 0; i < saveablesSnapshot.Length; i++)
                {
                    saveablesSnapshot[i]?.AllObjectsDependenciesRestored();
                }
            }
        }

        internal void DestroyChild(string childId, List<SaveableEntity> children = null)
        {
            if (string.IsNullOrEmpty(childId))
            {
                return;
            }

            if (children == null)
            {
                ObjectFinder.GetFirstChildLayerOfType(gameObject, s_childBuffer);
                children = s_childBuffer;
            }

            for (int i = 0; i < children.Count; i++)
            {
                SaveableEntity child = children[i];
                if (child != null && string.Equals(child.m_childID, childId, StringComparison.Ordinal))
                {
                    DestroyImmediate(child.gameObject);
                    break;
                }
            }

            if (ReferenceEquals(children, s_childBuffer))
            {
                s_childBuffer.Clear();
            }
        }

        private void AttachToParent(SaveableEntity parent, string parentPath)
        {
            if (parent == null)
            {
                return;
            }

            Transform targetTransform = parent.transform;
            if (!string.IsNullOrEmpty(parentPath))
            {
                Transform found = parent.transform.Find(parentPath);
                if (found != null)
                {
                    targetTransform = found;
                }
                else if (SaveSystem.VerboseLogging)
                {
                    Debug.LogWarning($"[FlexibleSave] Parent path '{parentPath}' not found under '{parent.name}', attaching directly.", this);
                }
            }

            if (transform.parent != targetTransform)
            {
                transform.SetParent(targetTransform, worldPositionStays: false);
            }

            m_parent = parent;

            ObjectFinder.GetFirstParentOfType(targetTransform.gameObject, s_saveableBuffer);
            if (s_saveableBuffer.Count == 1)
            {
                ISaveable receiver = s_saveableBuffer[0];
                s_saveableBuffer.Clear();
                receiver?.GotAddedAsChild(gameObject, targetTransform.gameObject);
            }
            else if (s_saveableBuffer.Count > 1)
            {
                if (!s_saveableSnapshotInUse)
                {
                    s_saveableSnapshotInUse = true;
                    s_saveableSnapshotBuffer.Clear();
                    s_saveableSnapshotBuffer.AddRange(s_saveableBuffer);
                    s_saveableBuffer.Clear();
                    try
                    {
                        for (int i = 0; i < s_saveableSnapshotBuffer.Count; i++)
                        {
                            s_saveableSnapshotBuffer[i]?.GotAddedAsChild(gameObject, targetTransform.gameObject);
                        }
                    }
                    finally
                    {
                        s_saveableSnapshotBuffer.Clear();
                        s_saveableSnapshotInUse = false;
                    }
                }
                else
                {
                    ISaveable[] receivers = s_saveableBuffer.ToArray();
                    s_saveableBuffer.Clear();
                    for (int i = 0; i < receivers.Length; i++)
                    {
                        receivers[i]?.GotAddedAsChild(gameObject, targetTransform.gameObject);
                    }
                }
            }
            else
            {
                s_saveableBuffer.Clear();
            }
        }

        private List<string> ComputeDeletedChildren(List<SaveableEntity> directChildren)
        {
            if (m_knownChildIds.Count == 0)
            {
                return null;
            }

            List<string> deleted = null;
            for (int i = 0; i < m_knownChildIds.Count; i++)
            {
                string knownId = m_knownChildIds[i];
                bool stillPresent = false;
                if (directChildren != null)
                {
                    for (int j = 0; j < directChildren.Count; j++)
                    {
                        SaveableEntity child = directChildren[j];
                        if (child != null && string.Equals(child.m_childID, knownId, StringComparison.Ordinal))
                        {
                            stillPresent = true;
                            break;
                        }
                    }
                }

                if (!stillPresent)
                {
                    deleted ??= new List<string>();
                    deleted.Add(knownId);
                }
            }

            return deleted;
        }

        private static string GetComponentKey(ISaveable saveable)
        {
            string key = saveable.SaveKey;
            return !string.IsNullOrEmpty(key) ? key : saveable.GetType().FullName;
        }

        private static string ComputeRelativeParentPath(Transform root, Transform targetParent)
        {
            if (root == null || targetParent == null || targetParent == root)
            {
                return null;
            }

            s_pathSegments.Clear();
            Transform current = targetParent;
            while (current != null && current != root)
            {
                s_pathSegments.Add(current);
                current = current.parent;
            }

            if (current != root)
            {
                s_pathSegments.Clear();
                return null;
            }

            s_pathBuilder.Clear();
            for (int i = s_pathSegments.Count - 1; i >= 0; i--)
            {
                if (s_pathBuilder.Length > 0)
                {
                    s_pathBuilder.Append('/');
                }
                s_pathBuilder.Append(s_pathSegments[i].name);
            }

            s_pathSegments.Clear();
            return s_pathBuilder.ToString();
        }
    }
}
