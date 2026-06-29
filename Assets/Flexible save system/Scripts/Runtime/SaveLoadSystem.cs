using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;
using static SaveLoadSystem.SaveableEntity;

namespace SaveLoadSystem {
    
    [DisallowMultipleComponent]
    public class SaveLoadSystem : MonoBehaviour
    {
        static SaveLoadSystem m_instance;
        string m_basePath;
        [SerializeField] string m_savePath = "Saves/";
        [SerializeField] string m_saveName = "save.save";
        [SerializeField] bool m_useEncryption = false;
        [SerializeField] string m_encryptionKey = "password";

        private void Awake()
        {
            m_instance = this;
            m_basePath = Application.persistentDataPath + "/";
        }
        private void OnDestroy()
        {
            SaveableEntityManager.Clear();
        }

        public static SaveLoadSystem instance
        {
            get
            {
                if (m_instance == null)
                {
#if UNITY_2021_3_OR_NEWER
                    var list = FindObjectsByType<SaveLoadSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (list.Length > 0)
                        m_instance = list[0];
#else
                    m_instance = FindObjectOfType<SaveLoadSystem>();
#endif
                }
                
                if (m_instance == null)
                    Debug.LogWarning("No instance of SaveLoadSystem in the scene");
                return m_instance;
            }
        }




        public static string basePath
        {
            get
            {
                if (instance == null) return Application.persistentDataPath + "/";
                return instance.m_basePath;
            }
            set
            {
                if (instance == null) return;
                instance.m_basePath = value;
            }
        }
        public static string savePath
        {
            get
            {
                if (instance == null) return null;
                return instance.m_savePath;
            }
            set
            {
                if (instance == null) return;
                instance.m_savePath = value;
            }
        }
        public static string saveName
        {
            get
            {
                if (instance == null) return null;
                return instance.m_saveName;
            }
            set
            {
                if (instance == null) return;
                instance.m_saveName = value;
            }
        }
        public static string fullSavePath
        {
            get
            {
                if (instance == null) return Application.persistentDataPath + "/saves/save.save";
                return instance.m_basePath + instance.m_savePath + instance.m_saveName;
            }
        }

        public static void SaveNew()
        {
            long startT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
            SaveablePrefabs.UpdateTable();
            Dictionary<string, object> state = new Dictionary<string, object>();
            SaveState(state);
            SaveFile(state);
            long endT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Debug.Log("Save time: " + (endT - startT) + "ms");
        }

        public static void Save()
        {
            long startT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var state = LoadFile();
            SaveablePrefabs.UpdateTable();
            SaveState(state);
            SaveFile(state);
            long endT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Debug.Log("Save time: " + (endT - startT) + "ms");
        }
        public static void Load()
        {
            long startT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var state = LoadFile();
            if(File.Exists(fullSavePath))
            {
                SaveablePrefabs.UpdateTable();
                LoadState(state);
                long endT = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
                Debug.Log("Loading time: " + (endT - startT) + "ms");
            }            
        }
        public static void Delete()
        {
            if (File.Exists(fullSavePath))
                File.Delete(fullSavePath);
        }

        public Coroutine LoadAsync(System.Action<float> onProgress, System.Action onComplete)
        {
            return StartCoroutine(LoadAsyncRoutine(onProgress, onComplete));
        }

        private System.Collections.IEnumerator LoadAsyncRoutine(System.Action<float> onProgress, System.Action onComplete)
        {
            var state = LoadFile();
            if (File.Exists(fullSavePath))
            {
                SaveablePrefabs.UpdateTable();
                yield return SaveableEntityManager.GlobalCreateFromSaveAsync(state, onProgress);
            }
            onComplete?.Invoke();
        }

        static void SaveFile(object state)
        {
            if (!Directory.Exists(basePath + savePath))
                Directory.CreateDirectory(basePath + savePath);

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented,
                // Security: Avoid TypeNameHandling.All to reduce deserialization attack surface (arbitrary code execution risk).
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new PrivateFieldContractResolver()
            };

            string json = JsonConvert.SerializeObject(state, settings);

            SaveLoadSystem currentInstance = instance;
            bool useEncryption = currentInstance != null && currentInstance.m_useEncryption;
            string encryptionKey = currentInstance != null ? currentInstance.m_encryptionKey : string.Empty;

            if (useEncryption)
            {
                json = EncryptionUtility.Encrypt(json, encryptionKey);
            }

            File.WriteAllText(fullSavePath, json);
        }

        static Dictionary<string, object> LoadFile()
        {
            if (!File.Exists(fullSavePath))
            {
                Debug.Log("No save file found");
                return new Dictionary<string, object>();
            }

            string json = File.ReadAllText(fullSavePath);

            SaveLoadSystem currentInstance = instance;
            bool useEncryption = currentInstance != null && currentInstance.m_useEncryption;
            string encryptionKey = currentInstance != null ? currentInstance.m_encryptionKey : string.Empty;

            if (useEncryption)
            {
                try
                {
                    json = EncryptionUtility.Decrypt(json, encryptionKey);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Failed to decrypt save file. Check your encryption key. Error: " + e.Message);
                    return new Dictionary<string, object>();
                }
            }

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                // Security: Avoid TypeNameHandling.All to reduce deserialization attack surface (arbitrary code execution risk).
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new PrivateFieldContractResolver()
            };

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json, settings);
            }
            catch (JsonSerializationException e)
            {
                Debug.LogError($"Failed to deserialize save file due to a type mismatch. This can happen after code refactoring. " +
                               $"Consider deleting the old save file. Error: {e.Message}");
                return new Dictionary<string, object>();
            }
        }

        static void SaveState(Dictionary<string, object> state)
        {
            SaveableEntityManager.GlobalSaveState(state);
        }

        static void LoadState(Dictionary<string, object> state)
        {
            SaveableEntityManager.GlobalCreateFromSave(state);
        }
    }
}