using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FlexibleSave.Enhanced
{
    public static class ObjectFinder
    {
        public static List<T> GetFirstParentOfType<T>(GameObject root)
        {
            var list = new List<T>();
            GetFirstParentOfTypeInternal(root, list);
            return list;
        }

        public static void GetFirstParentOfType<T>(GameObject root, List<T> results)
        {
            results.Clear();
            GetFirstParentOfTypeInternal(root, results);
        }

        private static void GetFirstParentOfTypeInternal<T>(GameObject current, List<T> list)
        {
            while (current != null)
            {
                current.GetComponents(list);
                if (list.Count > 0)
                {
                    return;
                }

                Transform parent = current.transform.parent;
                current = parent != null ? parent.gameObject : null;
            }
        }

        public static List<T> GetFirstChildLayerOfType<T>(GameObject root) where T : Component
        {
            var list = new List<T>();
            GetFirstChildLayerOfType(root, list);
            return list;
        }

        public static void GetFirstChildLayerOfType<T>(GameObject root, List<T> results) where T : Component
        {
            results.Clear();
            if (root == null)
            {
                return;
            }

            GetFirstChildLayerOfTypeInternal(root.transform, results);
        }

        private static void GetFirstChildLayerOfTypeInternal<T>(Transform root, List<T> list) where T : Component
        {
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.TryGetComponent<T>(out T component))
                {
                    list.Add(component);
                }
                else
                {
                    GetFirstChildLayerOfTypeInternal(child, list);
                }
            }
        }
    }

    public static partial class IDFinder
    {
        private sealed class GameObjectCacheEntry
        {
            public Dictionary<string, Object> ById;
            public int FrameBuilt;
        }

        [AutoStaticsCleanup]
        private static Dictionary<Type, Dictionary<string, Object>> s_scriptableObjectCache =
            new Dictionary<Type, Dictionary<string, Object>>();

        [AutoStaticsCleanup]
        private static Dictionary<Type, GameObjectCacheEntry> s_gameObjectCache =
            new Dictionary<Type, GameObjectCacheEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeHooks()
        {
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneChanged(Scene scene, LoadSceneMode mode)
        {
            ClearSceneCache();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            ClearSceneCache();
        }

        public static void ClearCache()
        {
            s_scriptableObjectCache?.Clear();
            s_gameObjectCache?.Clear();
        }

        internal static void ClearSceneCache()
        {
            s_gameObjectCache?.Clear();
        }

        public static T GetScriptableObject<T>(string id) where T : Object, ISaveID
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            s_scriptableObjectCache ??= new Dictionary<Type, Dictionary<string, Object>>();
            Type type = typeof(T);

            if (!s_scriptableObjectCache.TryGetValue(type, out Dictionary<string, Object> map) || map == null)
            {
                map = BuildScriptableObjectMap<T>();
                s_scriptableObjectCache[type] = map;
            }

            if (map.TryGetValue(id, out Object cached))
            {
                if (cached != null && cached is T typed)
                {
                    return typed;
                }

                // Cached Unity Object was unloaded/destroyed; rebuild once.
                map = BuildScriptableObjectMap<T>();
                s_scriptableObjectCache[type] = map;
                if (map.TryGetValue(id, out cached) && cached != null && cached is T refreshed)
                {
                    return refreshed;
                }
            }

            return null;
        }

        public static T GetGameObject<T>(string id) where T : Object, ISaveID
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            s_gameObjectCache ??= new Dictionary<Type, GameObjectCacheEntry>();
            Type type = typeof(T);
            int currentFrame = Application.isPlaying ? Time.frameCount : -1;

            if (s_gameObjectCache.TryGetValue(type, out GameObjectCacheEntry entry) && entry != null)
            {
                if (entry.ById.TryGetValue(id, out Object cached))
                {
                    if (cached != null &&
                        cached is T typed &&
                        string.Equals(typed.GetID(), id, StringComparison.Ordinal))
                    {
                        return typed;
                    }
                }

                if (Application.isPlaying && entry.FrameBuilt == currentFrame)
                {
                    return null;
                }
            }

            entry = BuildGameObjectEntry<T>(currentFrame);
            s_gameObjectCache[type] = entry;

            if (entry.ById.TryGetValue(id, out Object found) && found != null && found is T result)
            {
                return result;
            }

            return null;
        }

        public static GameObject GetPrefab(string id)
        {
            return SaveablePrefabs.GetPrefab(id);
        }

        private static Dictionary<string, Object> BuildScriptableObjectMap<T>() where T : Object, ISaveID
        {
            var map = new Dictionary<string, Object>(StringComparer.Ordinal);
            T[] objs = Resources.LoadAll<T>(string.Empty);
            for (int i = 0; i < objs.Length; i++)
            {
                T obj = objs[i];
                if (obj == null)
                {
                    continue;
                }

                string objId = obj.GetID();
                if (!string.IsNullOrEmpty(objId) && !map.ContainsKey(objId))
                {
                    map.Add(objId, obj);
                }
            }

            return map;
        }

        private static GameObjectCacheEntry BuildGameObjectEntry<T>(int frame) where T : Object, ISaveID
        {
            var map = new Dictionary<string, Object>(StringComparer.Ordinal);
            T[] objs = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < objs.Length; i++)
            {
                T obj = objs[i];
                if (obj == null)
                {
                    continue;
                }

                string objId = obj.GetID();
                if (!string.IsNullOrEmpty(objId) && !map.ContainsKey(objId))
                {
                    map.Add(objId, obj);
                }
            }

            return new GameObjectCacheEntry
            {
                ById = map,
                FrameBuilt = frame
            };
        }
    }
}
