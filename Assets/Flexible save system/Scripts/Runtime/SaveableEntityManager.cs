using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveLoadSystem {
    partial class SaveableEntity
    {
        public class SaveableEntityManager : UnityEngine.Object
        {
            // Dictionary with all SaveableEntities
            static Dictionary<string, SaveableEntity> m_allSaveables = new Dictionary<string, SaveableEntity>();

            // Tracks all currently active SaveableEntities (registered in OnEnable/OnDisable)
            static HashSet<SaveableEntity> activeEntities = new HashSet<SaveableEntity>();

            // List with all deleted SaveableEntities to remove them when saving
            private static List<string> m_deletedObjects = new List<string>();

            // List with all used IDs to generate unique IDs
            private static Dictionary<string, bool> m_usedIDs = new Dictionary<string, bool>();

            
            public static void ClearUsedIDs()
            {
                m_usedIDs.Clear();
            }
            public static void ClearDeletedObjects()
            {
                m_deletedObjects.Clear();
            }
            public static void ClearAllSaveables()
            {
                m_allSaveables.Clear();
            }
            public static void Clear()
            {
                ClearUsedIDs();
                ClearDeletedObjects();
                ClearAllSaveables();
            }


            // Registers an active SaveableEntity
            public static void RegisterEntity(SaveableEntity entity)
            {
                if (entity == null)
                    return;
                activeEntities.Add(entity);
            }

            // Unregisters an inactive/destroyed SaveableEntity
            public static void UnregisterEntity(SaveableEntity entity)
            {
                if (entity == null)
                    return;
                activeEntities.Remove(entity);
            }

            // Adds a SaveableEntity to the dictionary
            public static void AddSaveable(SaveableEntity saveable)
            {
                m_allSaveables[saveable.GetID()] = saveable;
                m_usedIDs[saveable.GetID()] = true;
            }

            // Checks if the ID already exists
            public static bool IDExists(string ID)
            {
                return m_usedIDs.ContainsKey(ID);
            }

            // Returns the SaveableEntity with the given ID
            public static SaveableEntity GetSaveable(string ID)
            {
                if (m_allSaveables.ContainsKey(ID))
                    return m_allSaveables[ID];
                return null;
            }

            // Notifies the SaveableEntityManager that id of a SaveableEntity has been changed
            public static bool replaceID(string oldID, string newID)
            {
                if (m_allSaveables.ContainsKey(oldID))
                {
                    SaveableEntity saveable = m_allSaveables[oldID];
                    m_allSaveables.Remove(oldID);
                    m_allSaveables[newID] = saveable;

                    if (!m_usedIDs.ContainsKey(newID))
                    {
                        m_usedIDs[newID] = true;
                    }

                    if (m_usedIDs.ContainsKey(oldID))
                    {
                        m_usedIDs.Remove(oldID);
                    }

                    return true;
                }
                return false;
            }

            // Notifies the SaveableEntityManager that a SaveableEntity has been deleted
            public static void OnSaveableDeleted(SaveableEntity saveable)
            {
                m_deletedObjects.Add(saveable.GetID());
                m_allSaveables.Remove(saveable.GetID());
                m_usedIDs[saveable.GetID()] = true;
            }



            // Fills the savable dictionary object with all active SaveableEntities
            public static void GlobalSaveState(Dictionary<string, object> state)
            {
                // Remove deleted objects
                foreach (var ID in m_deletedObjects)
                {
                    state.Remove(ID);
                }
                m_deletedObjects.Clear();

                // Populate the dictionary from registered active entities.
                m_allSaveables.Clear();
                List<SaveableEntity> invalidEntities = null;
                foreach (var entity in activeEntities)
                {
                    if (entity == null)
                    {
                        if (invalidEntities == null)
                            invalidEntities = new List<SaveableEntity>();
                        invalidEntities.Add(entity);
                        continue;
                    }

                    if (!m_allSaveables.ContainsKey(entity.GetID()))
                    {
                        m_allSaveables.Add(entity.GetID(), entity);
                    }
                }

                // Remove stale/null references from the active registry.
                if (invalidEntities != null)
                {
                    foreach (var invalid in invalidEntities)
                    {
                        activeEntities.Remove(invalid);
                    }
                }

                // First, reset parent links so stale references from previous hierarchy states are cleared.
                foreach (var saveable in m_allSaveables.Values)
                {
                    saveable.m_parent = null;
                }

                // Then, establish current parent-child relationships for tracked entities
                foreach (var saveable in m_allSaveables.Values)
                {
                    foreach (var child in ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(saveable.gameObject))
                    {
                        if (child != null && m_allSaveables.ContainsKey(child.GetID()))
                        {
                            child.m_parent = saveable;
                        }
                    }
                }

                // Finally, save state starting from root objects
                foreach (var saveable in m_allSaveables.Values)
                {
                    if (saveable.m_parent == null)
                    {
                        object d = saveable.SaveState();
                        if (d != null)
                            state[saveable.GetID()] = d;
                    }
                }
            }

            // Loads all saved Objects
            public static void GlobalCreateFromSave(Dictionary<string, object> state)
            {
                m_deletedObjects.Clear();
                m_allSaveables.Clear();

                // Pre-populate the dictionary with ALL saveable entities in the scene, including inactive ones.
#if UNITY_2021_3_OR_NEWER
                SaveableEntity[] allEntitiesInScene = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                SaveableEntity[] allEntitiesInScene = FindObjectsOfType<SaveableEntity>();
#endif
                foreach (var entity in allEntitiesInScene)
                {
                    if (!m_allSaveables.ContainsKey(entity.GetID()))
                    {
                        m_allSaveables.Add(entity.GetID(), entity);
                    }
                }

                // Delete objects in the scene that are not in the save file
                foreach (var entity in allEntitiesInScene)
                {
                    if (entity != null && !state.ContainsKey(entity.GetID()))
                    {
                        Destroy(entity.gameObject);
                    }
                }

                GlobalPreLoad();

                // Create and load objects from the save file
                foreach (var savedState in state)
                {
                    m_usedIDs[savedState.Key] = true;
                    CreateFromSave(savedState.Value);
                }
                GlobalAllObjectsStateLoaded();
                GlobalAllObjectsDependenciesRestored();
            }

            public static System.Collections.IEnumerator GlobalCreateFromSaveAsync(Dictionary<string, object> state, System.Action<float> onProgress)
            {
                m_deletedObjects.Clear();
                m_allSaveables.Clear();

                onProgress?.Invoke(0.05f);
                yield return null;

#if UNITY_2021_3_OR_NEWER
                SaveableEntity[] allEntitiesInScene = FindObjectsByType<SaveableEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                SaveableEntity[] allEntitiesInScene = FindObjectsOfType<SaveableEntity>();
#endif
                foreach (var entity in allEntitiesInScene)
                {
                    if (!m_allSaveables.ContainsKey(entity.GetID()))
                    {
                        m_allSaveables.Add(entity.GetID(), entity);
                    }
                }
                
                onProgress?.Invoke(0.1f);
                yield return null;

                foreach (var entity in allEntitiesInScene)
                {
                    if (entity != null && !state.ContainsKey(entity.GetID()))
                    {
                        Destroy(entity.gameObject);
                    }
                }

                onProgress?.Invoke(0.15f);
                yield return null;

                GlobalPreLoad();

                int totalObjects = state.Count;
                int processedObjects = 0;

                foreach (var savedState in state)
                {
                    m_usedIDs[savedState.Key] = true;
                    CreateFromSave(savedState.Value);
                    processedObjects++;
                    onProgress?.Invoke(0.15f + (processedObjects / (float)totalObjects) * 0.5f); // 15% to 65%
                    yield return null;
                }

                GlobalAllObjectsStateLoaded();
                onProgress?.Invoke(0.85f);
                yield return null;

                GlobalAllObjectsDependenciesRestored();
                onProgress?.Invoke(1f);
                yield return null;
            }

            public static void GlobalPreLoad()
            {
                foreach (var saveable in m_allSaveables)
                {
                    saveable.Value.m_dictionary = null;
                }
            }

            public static void GlobalAllObjectsStateLoaded()
            {
                foreach (var saveable in m_allSaveables)
                {
                    saveable.Value.TransformUpdate();
                }
                foreach (var saveable in m_allSaveables)
                {
                    saveable.Value.AllObjectsStateLoaded();
                }
            }

            public static void GlobalAllObjectsDependenciesRestored()
            {
                foreach (var saveable in m_allSaveables)
                {
                    saveable.Value.AllObjectsDependenciesRestored();
                }
            }


            // Generates a unique ID
            public static string GenerateUniqueID()
            {
                string ID = Guid.NewGuid().ToString();
                while (m_usedIDs.ContainsKey(ID))
                {
                    ID = Guid.NewGuid().ToString();
                }
                m_usedIDs[ID] = true;
                return ID;
            }

            // Generates a unique ID based on the name: "Name", "Name_1", ...
            public static string GenerateUniqueID_nameBased(string name)
            {
                int counter = 1;
                string ID = name;
                while (m_usedIDs.ContainsKey(ID))
                {
                    ID = name + "_" + counter;
                    counter++;
                }
                m_usedIDs[ID] = true;
                return ID;
            }




            // Creates a instance of the saved gameObject, using its prefab and stored data
            // This is a recursive function, if you have multiple prefab objects as childs of each other, 
            // it will instantiate all them and set the correct parent of each instantiated child.
            public static GameObject CreateFromSave(object state, SaveableEntity parent = null)
            {
                GameObject obj = null;
                if (state == null)
                {
                    Debug.LogWarning("Can't load state, state == null.");
                    return null;
                }
                var stateDictionary = (Dictionary<string, object>)state;

                string objectName = "unknown";
                string parentName = "unknown";
                SaveableEntity savableOfInstance;
                if (!stateDictionary.TryGetValue("ObjectMetadata", out object meta) || meta == null)
                {
                    Debug.LogWarning("Something went wrong while trying to create an object. Its metadata is missing.");
                    return null;
                }

                ObjectMetadata metadata = (ObjectMetadata)meta;
                objectName = metadata.name;
                parentName = metadata.parentName;

                if (metadata.needsToBeReinstantiated && metadata.hasPrefab)
                {
                    if (parent != null && metadata.thisChildID != "")
                        parent.DestroyChild(metadata.thisChildID);
                    obj = Reinstantiate(metadata);
                }
                else
                {
                    if (metadata.thisChildID != "" && parent != null)
                    {
                        List<SaveableEntity> childsOfParent = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(parent.gameObject);
                        foreach (var child in childsOfParent)
                            if (child.m_childID == metadata.thisChildID)
                                obj = child.gameObject;

                        if (obj == null)
                            Debug.Log("Can't find child with same childID in the prefab. childID: " + metadata.thisChildID, parent);
                    }
                    else
                    {
                        SaveableEntity o = FindID(metadata.thisID);
                        if (o != null)
                            obj = o.gameObject;
                        else if (metadata.hasPrefab)
                            obj = Reinstantiate(metadata);
                    }
                }

                if (obj == null)
                {
                    Debug.LogWarning($"Failed to find or instantiate object '{objectName}' (ID: {metadata.thisID}) as child of '{parentName}'.");
                    return null;
                }

                savableOfInstance = obj.GetComponent<SaveableEntity>();
                if (savableOfInstance == null)
                {
                    Debug.LogWarning("Prefab of saveable Object seems not to have the component SavableEntity", obj);
                    return null;
                }

                savableOfInstance.m_dictionary = stateDictionary;
                savableOfInstance.metadata = metadata;
                savableOfInstance.LoadState();
                savableOfInstance.AttachToParent(parent);

                if (stateDictionary.TryGetValue("ChildData", out object d))
                {
                    if (d != null)
                    {
                        List<object> childData = (List<object>)d;
                        foreach (var child in childData)
                        {
                            CreateFromSave(child, savableOfInstance);
                        }
                    }
                }

                return obj;
            }
            // Check if the gameObject already exists and if it not exists,
            // it will be generated
            static GameObject CreateIfIdNotExists(ObjectMetadata meta)
            {
                SaveableEntity obj = FindID(meta.thisID);
                if (obj != null)
                    return obj.gameObject;
                GameObject prefab = SaveablePrefabs.GetPrefab(meta.prefabID);
                if (prefab == null)
                    return null;
                GameObject newObj = Instantiate(prefab);
                SaveableEntity newSav = newObj.GetComponent<SaveableEntity>();
                SetupInstantiated(meta, newSav);
                return newObj;
            }

            // Check if the gameObject already exists and if it exists,
            // it will be destroyed and regenerated
            static GameObject Reinstantiate(ObjectMetadata meta)
            {
                SaveableEntity obj = FindID(meta.thisID);

                if (obj != null)
                    DestroyImmediate(obj.gameObject);
                
                GameObject prefab = SaveablePrefabs.GetPrefab(meta.prefabID);
                if (prefab == null)
                {
                    Debug.LogWarning("Can't load object, no prefab was found for " + meta.name);
                    return null;
                }
                GameObject newObj = null;
                newObj = Instantiate(prefab);

                SaveableEntity newSav = newObj.GetComponent<SaveableEntity>();
                SetupInstantiated(meta, newSav);
                return newObj;
            }

            static void SetupInstantiated(ObjectMetadata meta, SaveableEntity newSav)
            {
                newSav.SetID(meta.thisID);
                newSav.PrefabChildIdentifier();
                if (meta.deletedChilds != null && meta.deletedChilds.Count > 0)
                {
                    List<SaveableEntity> childs = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(newSav.gameObject);
                    foreach (var child in meta.deletedChilds)
                    {
                        newSav.DestroyChild(child, childs);
                    }
                }
            }
        }
    }
}