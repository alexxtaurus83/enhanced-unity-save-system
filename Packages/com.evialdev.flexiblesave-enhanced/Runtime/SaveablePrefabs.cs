using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace FlexibleSave.Enhanced
{
    [CreateAssetMenu(fileName = "SaveablePrefabs", menuName = "Flexible Save Enhanced/SaveablePrefabs")]
    public partial class SaveablePrefabs : ScriptableObject
    {
        public const string ResourcesPath = "FlexibleSave/SaveablePrefabs";

        [AutoStaticsCleanup]
        private static SaveablePrefabs s_instance;

        [SerializeField] private List<SaveableEntity> m_prefabs = new List<SaveableEntity>();

        private Dictionary<string, SaveableEntity> m_lookup;
        private bool m_lookupBuilt;

        public static SaveablePrefabs Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = Resources.Load<SaveablePrefabs>(ResourcesPath);
                }

                return s_instance;
            }
        }

        public List<SaveableEntity> Prefabs
        {
            get
            {
                if (m_prefabs == null)
                {
                    m_prefabs = new List<SaveableEntity>();
                }

                return m_prefabs;
            }
        }

        internal static void SetInstanceForTesting(SaveablePrefabs instance)
        {
            s_instance = instance;
            if (instance != null)
            {
                instance.InvalidateLookup();
            }
        }

        private void OnEnable()
        {
            InvalidateLookup();
        }

        private void OnValidate()
        {
            InvalidateLookup();
        }

        public void InvalidateLookup()
        {
            m_lookupBuilt = false;
        }

        public void RebuildLookup()
        {
            if (m_lookup == null)
            {
                m_lookup = new Dictionary<string, SaveableEntity>(StringComparer.Ordinal);
            }
            else
            {
                m_lookup.Clear();
            }

            if (m_prefabs != null)
            {
                for (int i = 0; i < m_prefabs.Count; i++)
                {
                    SaveableEntity entity = m_prefabs[i];
                    if (entity == null)
                    {
                        continue;
                    }

                    string prefabId = entity.PrefabID;
                    if (string.IsNullOrEmpty(prefabId))
                    {
                        continue;
                    }

                    m_lookup[prefabId] = entity;
                }
            }

            m_lookupBuilt = true;
        }

        private void EnsureLookup()
        {
            if (!m_lookupBuilt || m_lookup == null)
            {
                RebuildLookup();
            }
        }

        public static GameObject GetPrefab(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId))
            {
                return null;
            }

            SaveablePrefabs instance = Instance;
            if (instance == null)
            {
                return null;
            }

            instance.EnsureLookup();

            if (instance.m_lookup.TryGetValue(prefabId, out SaveableEntity prefab) && prefab != null)
            {
                return prefab.gameObject;
            }

            if (SaveSystem.VerboseLogging)
            {
                Debug.LogWarning($"[FlexibleSave] Prefab with ID '{prefabId}' was not found in SaveablePrefabs.");
            }

            return null;
        }
    }
}
