using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveLoadSystem {
    [DisallowMultipleComponent]
    public partial class SaveableEntity : MonoBehaviour, ISaveID
    {
        [Serializable]
        public struct Vector2Data
        {
            public float x, y;

            public Vector2Data(Vector2 vec)
            {
                x = vec.x;
                y = vec.y;
            }
            public void FromVector2(Vector2 v)
            {
                x = v.x;
                y = v.y;
            }
            public Vector2 ToVector2()
            {
                return new Vector2(x, y);
            }
        }
        [Serializable]
        public struct Vector3Data
        {
            public float x, y, z;

            public Vector3Data(Vector3 vec)
            {
                x = vec.x;
                y = vec.y;
                z = vec.z;
            }
            public void FromVector3(Vector3 v)
            {
                x = v.x;
                y = v.y;
                z = v.z;
            }
            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }
        }
        [Serializable]
        public struct Vector4Data
        {
            public float x, y, z, w;
            public void FromVector4(Vector4 v)
            {
                x = v.x;
                y = v.y;
                z = v.z;
                w = v.w;
            }
            public void FromQuaternion(Quaternion q)
            {
                x = q.x;
                y = q.y;
                z = q.z;
                w = q.w;
            }
            public Vector4 ToVector4()
            {
                return new Vector4(x, y, z, w);
            }
            public Quaternion ToQuaternion()
            {
                return new Quaternion(x, y, z, w);
            }
        }




        [Serializable]
        public struct LocRotScale
        {
            public Vector3Data position;
            public Vector4Data rotation;
            public Vector3Data scale;
        }
        [Serializable]
        public struct TransformData
        {
            public LocRotScale local;
            //     public LocRotScale global;
        }
        [Serializable]
        public struct ObjectMetadata
        {
            public string name;       // Name of this gameObject
            public string thisID;     // ID of this gameObject
            public string parentID;   // ID of the next higher SaveableEntity 
            public string parentName; // Used to set the parent correct, may not a SaveableEntity, can be any gameObject
            public bool hasPrefab;
            public string prefabID;   // Prefab ID for instantiating this gameObject
            public string thisChildID; // ID for this in case this is a child of a SaveableEntity
            public bool saveTransform; 
            public TransformData? transformData; // Location, Rotation and scale of this gameObject
            public List<string> deletedChilds;
            public bool needsToBeReinstantiated;
        }

        SaveableEntity m_parent = null;

        [SerializeField] string m_ID;
        [SerializeField] string m_childID;
        [SerializeField] string m_prefabID;
        [SerializeField] bool m_saveTransform = true;

        ObjectMetadata m_metadata;
        Dictionary<string, object> m_dictionary;


        List<string> m_childIDsOfThis = new List<string>();

        private void Awake()
        {
            // Check if the same ID already exists. This will be the case by cloning the GameObject
            // If so, generate a unique ID for this
            if (m_ID != "")
            {
                // When the object is not already registered in the manager but the ID already exists, generate a new one
                SaveableEntity fromManager = SaveableEntityManager.GetSaveable(m_ID);
                if (fromManager != null && fromManager != this &&
                   SaveableEntityManager.IDExists(m_ID))
                    GenerateID();
            }
            else
                GenerateID();
            if (m_prefabID != "")
                PrefabChildIdentifier();

            SaveableEntityManager.AddSaveable(this);
        }

        private void OnEnable()
        {
            SaveableEntityManager.RegisterEntity(this);
        }

        private void OnDisable()
        {
            SaveableEntityManager.UnregisterEntity(this);
        }
        

        private void OnDestroy()
        {
            SaveableEntityManager.UnregisterEntity(this);
            SaveableEntityManager.OnSaveableDeleted(this);
        }
        void PrefabChildIdentifier()
        {
            List<SaveableEntity> childs = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(gameObject);
            m_childIDsOfThis.Clear();
            foreach (var child in childs)
            {
                if (string.IsNullOrEmpty(child.m_childID))
                {
                    child.m_childID = Guid.NewGuid().ToString();
                }
                m_childIDsOfThis.Add(child.m_childID);
                child.PrefabChildIdentifier();
            }
        }
        List<string> GetDeletedChilds()
        {
            List<SaveableEntity> childs = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(gameObject);
            List<string> deleted = new List<string>();
            for (int i = 0; i < m_childIDsOfThis.Count; ++i)
            {
                bool match = false;
                for (int j = 0; j < childs.Count; ++j)
                {
                    if(childs[j].m_childID == m_childIDsOfThis[i])
                    {
                        match = true;
                        break;
                    }
                }
                if (!match)
                    deleted.Add(m_childIDsOfThis[i]);
            }
            return deleted;
        }
        

        public string GetID()
        {
            return m_ID;
        }
        public void SetID(string ID)
        {
            if(m_ID.CompareTo(ID) == 0)
            {
                return;
            }
            string oldID = m_ID;
            m_ID = ID;
            if(!SaveableEntityManager.replaceID(oldID, m_ID))
            {
                SaveableEntityManager.AddSaveable(this);
            }
        }
        public string GetPrefabID()
        {
            return m_prefabID;
        }
        public void SetSaveTransform(bool enable)
        {
            m_saveTransform = enable;
        }
        public bool GetSaveTransform()
        {
            return m_saveTransform;
        }
        public void ResetTransform()
        {
            if (m_metadata.transformData.HasValue)
            {
                transform.localPosition = m_metadata.transformData.Value.local.position.ToVector3();
                transform.localRotation = m_metadata.transformData.Value.local.rotation.ToQuaternion();
                transform.localScale = m_metadata.transformData.Value.local.scale.ToVector3();
            }
        }
        public ObjectMetadata metadata
        {
            get { return m_metadata; }
            set
            {
                m_metadata = value;
                gameObject.name = m_metadata.name;
                SetID(m_metadata.thisID);
                m_prefabID = m_metadata.prefabID;
                m_childID = m_metadata.thisChildID;
                m_saveTransform = m_metadata.saveTransform;
                if(m_saveTransform)
                    ResetTransform();
            }
        }

        [ContextMenu("Generate ID")]
        public void GenerateID()
        {
            SetID(SaveableEntityManager.GenerateUniqueID());
        }
        public void GenerateID_NameBased()
        {
            SetID(SaveableEntityManager.GenerateUniqueID_nameBased(gameObject.name));
        }

        // Creates a save object which will be stored in the binary file
        public object SaveState()
        {
            var state = new Dictionary<string, object>();
            string parentID = "";
            string parentName = "";
            if (m_parent != null)
            {
                parentID = m_parent.m_ID;
                parentName = transform.parent.name;
            }
            TransformData? transformData = null;
            if (m_saveTransform)
            {
                TransformData data = new TransformData();
                data.local.position.FromVector3(transform.localPosition);
                data.local.rotation.FromQuaternion(transform.localRotation);
                data.local.scale.FromVector3(transform.localScale);
                transformData = data;
            }

            bool hasPrefab = true;
            if (m_prefabID == "")
                hasPrefab = false;
            //if (hasPrefab)
            //    PrefabChildIdentifier();

            


            bool doNotSaveThis = false;
            bool needsReinstantiate = false;
            ISaveable[] interfaces = GetComponents<ISaveable>();
            if (interfaces.Length > 0)
                doNotSaveThis = true;
            for(int i=0; i<interfaces.Length; ++i)
            {
                if (interfaces[i].NeedsToBeSaved()/* || m_parent != null*/)
                {
                    try
                    {
                        state[interfaces[i].GetType().FullName] = interfaces[i].SaveState();
                    }
                    catch (System.NotImplementedException)
                    {
                        Debug.LogError($"The SaveState method of the component '{interfaces[i].GetType().FullName}' on GameObject '{gameObject.name}' is not implemented.", gameObject);
                        // Optionally, re-throw or handle as needed
                    }
                    doNotSaveThis = false;
                    needsReinstantiate |= interfaces[i].NeedsReinstantiation();
                }
            }
            List<object> childData = new List<object>();
            foreach (var saveable in ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(gameObject))
            {
                object o = saveable.SaveState();
                if(o != null)
                    childData.Add(o);
            }
            ObjectMetadata meta = new ObjectMetadata()
            {
                name = gameObject.name,
                thisID = m_ID,
                parentID = parentID,
                parentName = parentName,
                hasPrefab = hasPrefab,
                prefabID = m_prefabID,
                thisChildID = m_childID,
                saveTransform = m_saveTransform,
                transformData = transformData,
                deletedChilds = GetDeletedChilds(),
                needsToBeReinstantiated = needsReinstantiate
        };

            state["ObjectMetadata"] = meta;
            state["ChildData"] = childData;

            if (doNotSaveThis && childData.Count == 0)
                return null;
            /*  if (state.Count > 0 || childData.Count > 0)
              {
                  state["ObjectMetadata"] = meta;
                  state["ChildData"] = childData;
              }
              else return null;*/

            return state;
        }


        void DestroyChild(string childID, List<SaveableEntity> childs = null)
        {
            
            if (childs == null)
                childs = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(gameObject);
            for (int i = 0; i < childs.Count; ++i)
            {
                if (childID == childs[i].m_childID)
                {
                    DestroyImmediate(childs[i].gameObject);
                }
            }
        }

        void AttachToParent(SaveableEntity parent)
        {
            if (parent == null)
                return;
            if(m_parent != null)
            {
                if (m_parent.name == m_metadata.parentName)
                    return; // Parent already set
            }
            // Parent Saveable found
            // Search for exact parent Object...
           // ISaveable[] possibleEventReceiver = parent.gameObject.GetComponents<ISaveable>();

           /* List<SaveableEntity> childsOfParent = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(parent.gameObject);
            foreach (var child in childsOfParent)
                if (child.m_childID == metadata.thisChildID)
                    obj = child.gameObject;
                else
                {
                    Debug.Log("Can't find child with same childID in the prefab: ", parent);
                }*/

            GameObject parentObj = ObjectFinder.GetFirstChildWithNameRecusive(parent.gameObject, m_metadata.parentName);
            if (parentObj != null)
            {
                /*for (int i = 0; i < parentObj.transform.childCount; ++i)
                {
                    GameObject child = parentObj.transform.GetChild(i).gameObject;
                    if (child.name == transform.name)
                    {
                        SaveableEntity childS = child.GetComponent<SaveableEntity>();
                        if (childS != null)
                            if (childS.m_dictionary == null)// Replace the existing object with the new one
                            {
                                
                                Debug.LogWarning("Can't set parent, object with same name already exists in this place", this);
                                return;
                                break;
                            }
                    }
                }*/

                transform.SetParent(parentObj.transform);
                List<ISaveable> signalReceiver = ObjectFinder.GetFirstParentOfType<ISaveable>(parentObj);
                foreach (var receiver in signalReceiver)
                    receiver.GotAddedAsChild(transform.gameObject, parentObj);
            }
            else
            {
                transform.SetParent(parent.transform);
                List<ISaveable> signalReceiver = ObjectFinder.GetFirstParentOfType<ISaveable>(parent.gameObject);
                foreach (var receiver in signalReceiver)
                    receiver.GotAddedAsChild(transform.gameObject, parent.gameObject);
            }

            m_parent = parent;
        }

        // Find the SaveableEntity with the identification string 
        // Use this in the ISaveable interface methode: PostInstantiation(...) to build relations between objects
        public static SaveableEntity FindID(string id)
        {
            return SaveableEntityManager.GetSaveable(id);
        }

        // Loads the saved data in to all ISaveable components of this gameObject
        public void LoadState(object state)
        {
            m_dictionary = (Dictionary<string, object>)state;
            LoadState();
        }
        public void LoadState()
        {
            ISaveable[] interfaces = GetComponents<ISaveable>();
            for (int i = 0; i < interfaces.Length;  ++i)
            {
                string typeName = interfaces[i].GetType().FullName;
                if (m_dictionary.TryGetValue(typeName, out object savedState))
                {

                }
                if(savedState == null)
                {
                    Debug.LogWarning("Can't find the ISaveable interface: " + interfaces[i].GetType().ToString() + " with save index: " + i);
                }
                else
                    interfaces[i].LoadState(savedState);
            }
        }

        

        // Will set the relations of each saved Object
        //  -> Sets parent
        void PostLoad()
        {
            if (m_dictionary == null)
            {
                // This Object has no data in the stored file, so it can not be loaded
                //Debug.LogWarning("m_dictionary is null, did you forgot to call SaveableEntity::CreateFromSave() first?");
                return;
            }
            if (!string.IsNullOrEmpty(m_metadata.parentID))
            {
                SaveableEntity parentEntity = SaveableEntityManager.GetSaveable(m_metadata.parentID);
                if (parentEntity != null)
                {
                    AttachToParent(parentEntity);
                }
                else
                {
                    Debug.LogWarning($"Could not find parent with ID {m_metadata.parentID} for object {name} in the manager. This might happen if the parent is inactive.", this);
                }
            }
        }
        void TransformUpdate()
        {
            if (m_dictionary == null || m_parent != null)
                return;
            TransformUpdate_recursive(new List<SaveableEntity> { this });
        }
        void TransformUpdate_recursive(List<SaveableEntity> roots)
        {
            for (int i = 0; i < roots.Count; ++i)
            {
                List<SaveableEntity> childs = ObjectFinder.GetFirstChildLayerOfType<SaveableEntity>(roots[i].gameObject);

                TransformUpdate_recursive(childs);
                foreach (SaveableEntity child in childs)
                    child.ResetTransform();
            }
        }
        void AllObjectsStateLoaded()
        {
            if (m_dictionary == null)
                return;

            ISaveable[] interfaces = GetComponents<ISaveable>();
            for (int i = 0; i < interfaces.Length; ++i)
            {
                string typeName = interfaces[i].GetType().FullName;
                if (m_dictionary.TryGetValue(typeName, out object savedState))
                {
                    if (savedState != null)
                    {
                        interfaces[i].AllObjectsStateLoaded(savedState);
                    }
                }
            }
        }

        void AllObjectsDependenciesRestored()
        {
            if (m_dictionary == null)
                return;

            ISaveable[] interfaces = GetComponents<ISaveable>();
            foreach (var iSaveable in interfaces)
            {
                iSaveable.AllObjectsDependenciesRestored();
            }
        }


    }
}