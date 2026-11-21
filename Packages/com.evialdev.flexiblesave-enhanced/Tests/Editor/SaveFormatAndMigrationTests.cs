using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FlexibleSave.Enhanced.Security;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlexibleSave.Enhanced.Tests.Editor
{
    public class SaveFormatAndMigrationTests
    {
        private string m_tempDir;
        private readonly List<GameObject> m_createdObjects = new List<GameObject>();

        private sealed class KeyedSaveable : MonoBehaviour, ISaveable
        {
            public string CustomKey;
            public int Value;
            public string Label;

            public string SaveKey => CustomKey;

            [Serializable]
            public struct StateDto
            {
                public int value;
                public string label;
            }

            public object SaveState() => new StateDto { value = Value, label = Label };

            public void LoadState(object state)
            {
                StateDto dto = Enhanced.SaveState.As<StateDto>(state);
                Value = dto.value;
                Label = dto.label;
            }
        }

        private sealed class FailingReplaceFileSystem : IFileSystem
        {
            private readonly IFileSystem m_inner = PhysicalFileSystem.Instance;
            public bool ThrowOnWrite;
            public bool ThrowOnReplace;

            public bool Exists(string path) => m_inner.Exists(path);
            public void CreateDirectory(string path) => m_inner.CreateDirectory(path);
            public byte[] ReadAllBytes(string path) => m_inner.ReadAllBytes(path);
            public void Copy(string s, string d, bool o) => m_inner.Copy(s, d, o);
            public void Delete(string path) => m_inner.Delete(path);
            public void Move(string s, string d) => m_inner.Move(s, d);

            public void WriteAllBytes(string path, byte[] bytes)
            {
                if (ThrowOnWrite && path.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    throw new IOException("Simulated write failure.");
                }
                m_inner.WriteAllBytes(path, bytes);
            }

            public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName)
            {
                if (ThrowOnReplace)
                {
                    throw new IOException("Simulated replace failure.");
                }
                m_inner.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
            }
        }

        private sealed class FakeEncryptor : ISaveEncryptor
        {
            public string Id { get; }
            public FakeEncryptor(string id) => Id = id;
            public byte[] Encrypt(byte[] plain) => plain.Reverse().ToArray();
            public byte[] Decrypt(byte[] cipher) => cipher.Reverse().ToArray();
        }

        [SetUp]
        public void SetUp()
        {
            SaveableEntityRegistry.Clear();
            SaveSystem.Migrations = null;
            m_tempDir = Path.Combine(Path.GetTempPath(), "FlexibleSaveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.Migrations = null;
            for (int i = m_createdObjects.Count - 1; i >= 0; i--)
            {
                if (m_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_createdObjects[i]);
                }
            }
            m_createdObjects.Clear();
            SaveableEntityRegistry.Clear();

            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, recursive: true);
            }
        }

        private SaveSystem CreateSystem(string fileName = "save.json")
        {
            var go = new GameObject("SaveSystem");
            m_createdObjects.Add(go);
            var sys = go.AddComponent<SaveSystem>();
            sys.BasePath = m_tempDir;
            sys.Folder = string.Empty;
            sys.FileName = fileName;
            return sys;
        }

        private (SaveableEntity entity, KeyedSaveable comp) CreateEntity(string id, string customKey = null, int value = 0, string label = null)
        {
            var go = new GameObject("Entity_" + id);
            m_createdObjects.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            entity.IsManualId = true;
            entity.AssignInternalId(id);
            SaveableEntityRegistry.TryRegister(entity);

            var comp = go.AddComponent<KeyedSaveable>();
            comp.CustomKey = customKey;
            comp.Value = value;
            comp.Label = label;
            return (entity, comp);
        }

        [Test]
        public void Format_RoundTrip_HasExpectedSchema_AndNoTypeMetadata()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp1) = CreateEntity("hero", customKey: "hero.stats", value: 42, label: "knight");
            var (_, comp2) = CreateEntity("npc", customKey: null, value: 7, label: "merchant");

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.IsTrue(sys.HasLoaded);

            SaveResult saveRes = sys.Save();
            Assert.AreEqual(SaveStatus.Ok, saveRes.Status);
            Assert.IsTrue(File.Exists(sys.FullPath));

            string json = File.ReadAllText(sys.FullPath);
            Assert.IsFalse(json.Contains("$type"), "Save JSON must never contain $type metadata.");

            JObject root = JObject.Parse(json);
            Assert.AreEqual("FlexibleSave", root.Value<string>("format"));
            Assert.AreEqual(1, root.Value<int>("formatVersion"));
            Assert.Greater(root.Value<long>("savedAtUtc"), 0);
            Assert.IsNotNull(root["entities"]?["hero"]?["components"]?["hero.stats"]);
            Assert.IsNotNull(root["entities"]?["npc"]?["components"]?[typeof(KeyedSaveable).FullName]);

            comp1.Value = 0;
            comp1.Label = "wiped";
            comp2.Value = 0;

            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);
            Assert.AreEqual(2, loadRes.Restored);
            Assert.AreEqual(42, comp1.Value);
            Assert.AreEqual("knight", comp1.Label);
            Assert.AreEqual(7, comp2.Value);
            Assert.AreEqual("merchant", comp2.Label);
        }

        [Test]
        public void Save_BeforeLoad_IsBlocked()
        {
            SaveSystem sys = CreateSystem();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save blocked"));
            SaveResult res = sys.Save();
            Assert.AreEqual(SaveStatus.Blocked, res.Status);
            Assert.IsFalse(File.Exists(sys.FullPath));
        }

        [TestCase("", TestName = "Corrupt_EmptyFile")]
        [TestCase("null", TestName = "Corrupt_NullLiteral")]
        [TestCase("{\"format\":", TestName = "Corrupt_TruncatedJson")]
        [TestCase("{\"format\":\"WrongFormat\",\"formatVersion\":1}", TestName = "Corrupt_WrongFormat")]
        [TestCase("{\"format\":\"FlexibleSave\",\"formatVersion\":99,\"entities\":{}}", TestName = "Corrupt_NewerVersionWithoutMigration")]
        public void CorruptVariants_SetIsCorrupt_WriteBackupCopy_AndBlockSave(string content)
        {
            SaveSystem sys = CreateSystem();
            File.WriteAllText(sys.FullPath, content, Encoding.UTF8);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("is corrupt"));
            LoadResult loadRes = sys.Load();

            Assert.AreEqual(LoadStatus.Corrupt, loadRes.Status);
            Assert.IsTrue(sys.IsCorrupt);
            Assert.IsFalse(sys.HasLoaded);

            string[] corruptCopies = Directory.GetFiles(m_tempDir, "save.corrupt.*.json");
            Assert.AreEqual(1, corruptCopies.Length);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save blocked"));
            SaveResult saveRes = sys.Save();
            Assert.AreEqual(SaveStatus.Blocked, saveRes.Status);

            sys.Delete();
            Assert.IsFalse(sys.IsCorrupt);
            Assert.IsTrue(sys.HasLoaded);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
        }

        [Test]
        public void HeaderMismatch_ReportsBothIds_AsCorrupt()
        {
            SaveSystem sys = CreateSystem();
            var (_, _) = CreateEntity("e1", value: 1);
            sys.Encryptor = new FakeEncryptor("custom-a");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            sys.Encryptor = new FakeEncryptor("custom-b");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("custom-a.*custom-b"));
            LoadResult res = sys.Load();
            Assert.AreEqual(LoadStatus.Corrupt, res.Status);
            StringAssert.Contains("custom-a", res.Error);
            StringAssert.Contains("custom-b", res.Error);
        }

        [Test]
        public void Migration_UpgradesOlderVersion_WhenRegistered()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", customKey: "hero.stats");

            var legacyJson = new JObject
            {
                ["format"] = "FlexibleSave",
                ["formatVersion"] = 0,
                ["savedAtUtc"] = 123456,
                ["entities"] = new JObject
                {
                    ["hero"] = new JObject
                    {
                        ["meta"] = new JObject
                        {
                            ["id"] = "hero",
                            ["name"] = "Entity_hero"
                        },
                        ["components"] = new JObject
                        {
                            ["hero.stats"] = new JObject
                            {
                                ["value"] = 99,
                                ["label"] = "migrated"
                            }
                        }
                    }
                }
            };
            File.WriteAllText(sys.FullPath, legacyJson.ToString());

            SaveSystem.Migrations = (root, version) =>
            {
                Assert.AreEqual(0, version);
                root["formatVersion"] = 1;
                return root;
            };

            LoadResult res = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, res.Status);
            Assert.AreEqual(99, comp.Value);
            Assert.AreEqual("migrated", comp.Label);
        }

        [Test]
        public void BackupFallback_RestoresFromBak_WhenPrimaryCorrupt()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", value: 10, label: "first");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Value = 20;
            comp.Label = "second";
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            Assert.IsTrue(File.Exists(sys.FullPath + ".bak"));

            // Corrupt primary save.json; .bak still holds value=10, label="first"
            File.WriteAllText(sys.FullPath, "{corrupt_json");

            comp.Value = 999;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("restored from backup"));
            LoadResult res = sys.Load();

            Assert.AreEqual(LoadStatus.RestoredFromBackup, res.Status);
            Assert.IsFalse(sys.IsCorrupt);
            Assert.IsTrue(sys.HasLoaded);
            Assert.AreEqual(10, comp.Value);
            Assert.AreEqual("first", comp.Label);
            Assert.AreEqual(1, Directory.GetFiles(m_tempDir, "save.corrupt.*.json").Length);
        }

        [Test]
        public void AtomicWriteFailure_LeavesPreviousSaveIntact()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", value: 55, label: "safe");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            string originalContent = File.ReadAllText(sys.FullPath);

            var failingFs = new FailingReplaceFileSystem { ThrowOnWrite = true };
            sys.FileSystem = failingFs;
            bool failedRaised = false;
            sys.Failed += _ => failedRaised = true;

            comp.Value = 777;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save failed"));
            SaveResult failRes = sys.Save();

            Assert.AreEqual(SaveStatus.Failed, failRes.Status);
            Assert.IsTrue(failedRaised);
            Assert.AreEqual(originalContent, File.ReadAllText(sys.FullPath));
            Assert.IsFalse(File.Exists(sys.FullPath + ".tmp"));

            failingFs.ThrowOnWrite = false;
            failingFs.ThrowOnReplace = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save failed"));
            SaveResult replaceFail = sys.Save();

            Assert.AreEqual(SaveStatus.Failed, replaceFail.Status);
            Assert.AreEqual(originalContent, File.ReadAllText(sys.FullPath));
            Assert.IsFalse(File.Exists(sys.FullPath + ".tmp"));
        }

        private sealed class ThrowingSaveable : MonoBehaviour, ISaveable
        {
            public bool ShouldThrow;
            public int Value;

            public object SaveState()
            {
                if (ShouldThrow)
                {
                    throw new InvalidOperationException("Simulated SaveState failure.");
                }
                return Value;
            }

            public void LoadState(object state)
            {
                Value = Enhanced.SaveState.As<int>(state);
            }
        }

        [Test]
        public void CorruptLoad_RepeatedLoads_DoNotAccumulateCorruptCopies()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", value: 10, label: "ok");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            comp.Value = 20;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            File.WriteAllText(sys.FullPath, "{corrupt_json");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("restored from backup"));
            Assert.AreEqual(LoadStatus.RestoredFromBackup, sys.Load().Status);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("restored from backup"));
            Assert.AreEqual(LoadStatus.RestoredFromBackup, sys.Load().Status);

            string[] corruptCopies = Directory.GetFiles(m_tempDir, "save.corrupt.*.json");
            Assert.AreEqual(1, corruptCopies.Length);
        }

        [Test]
        public void DirectChild_OmitsParentPathFromJson()
        {
            SaveSystem sys = CreateSystem();
            var (parent, _) = CreateEntity("parent", value: 1);
            var (child, _) = CreateEntity("child", value: 2);
            child.transform.SetParent(parent.transform, false);

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            JObject root = JObject.Parse(File.ReadAllText(sys.FullPath));
            JToken childMeta = root["entities"]?["parent"]?["children"]?[0]?["meta"];
            Assert.IsNotNull(childMeta);
            Assert.IsNull(childMeta["parentPath"], "Direct child should omit parentPath from JSON.");
        }

        [Test]
        public void DuplicateSaveKey_LogsWarning()
        {
            SaveSystem sys = CreateSystem();
            var go = new GameObject("DupKeyObj");
            m_createdObjects.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            entity.IsManualId = true;
            entity.AssignInternalId("dup-key-entity");
            SaveableEntityRegistry.TryRegister(entity);

            var a = go.AddComponent<KeyedSaveable>();
            a.CustomKey = "shared.key";
            a.Value = 1;
            var b = go.AddComponent<KeyedSaveable>();
            b.CustomKey = "shared.key";
            b.Value = 2;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate SaveKey 'shared.key'"));
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
        }

        [Test]
        public void ThrowingSaveState_PreservesPriorSavedData()
        {
            SaveSystem sys = CreateSystem();
            var go = new GameObject("ThrowingEntity");
            m_createdObjects.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            entity.IsManualId = true;
            entity.AssignInternalId("throw-entity");
            SaveableEntityRegistry.TryRegister(entity);

            var comp = go.AddComponent<ThrowingSaveable>();
            comp.Value = 42;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Value = 0;
            comp.ShouldThrow = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Error in SaveState"));
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.ShouldThrow = false;
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            Assert.AreEqual(42, comp.Value);
        }

        [Test]
        public void SaveStateAs_HandlesTypedJTokenAndNull()
        {
            Assert.AreEqual(12, Enhanced.SaveState.As<int>(12));
            Assert.AreEqual(12, Enhanced.SaveState.As<int>(new JValue(12)));
            Assert.AreEqual(0, Enhanced.SaveState.As<int>(JValue.CreateNull()));
            Assert.IsNull(Enhanced.SaveState.As<string>(null));
        }

        [Test]
        public void MarkDirty_And_Save_UpdatesIsDirtyFlag()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", value: 10);

            Assert.IsFalse(sys.IsDirty);
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.IsFalse(sys.IsDirty);

            sys.MarkDirty();
            Assert.IsTrue(sys.IsDirty);

            comp.Value = 25;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            Assert.IsFalse(sys.IsDirty);
        }

        [Test]
        public void SaveFailure_And_BlockedSave_PreserveIsDirty()
        {
            SaveSystem sys = CreateSystem();
            CreateEntity("hero", value: 10);

            sys.MarkDirty();
            Assert.IsTrue(sys.IsDirty);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save blocked"));
            Assert.AreEqual(SaveStatus.Blocked, sys.Save().Status);
            Assert.IsTrue(sys.IsDirty, "Blocked save must not clear IsDirty.");

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.IsFalse(sys.IsDirty, "Load must reset IsDirty.");

            var failingFs = new FailingReplaceFileSystem { ThrowOnWrite = true };
            sys.FileSystem = failingFs;
            sys.MarkDirty();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save failed"));
            Assert.AreEqual(SaveStatus.Failed, sys.Save().Status);
            Assert.IsTrue(sys.IsDirty, "Failed save must preserve IsDirty.");

            failingFs.ThrowOnWrite = false;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            Assert.IsFalse(sys.IsDirty);

            sys.MarkDirty();
            sys.Delete();
            Assert.IsFalse(sys.IsDirty, "Delete must reset IsDirty.");
        }

        [Test]
        public void LifecycleHandlers_SaveOnlyWhenDirtyAndLoaded()
        {
            SaveSystem sys = CreateSystem();
            var (_, comp) = CreateEntity("hero", value: 1);

            MethodInfo onFocus = typeof(SaveSystem).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onPause = typeof(SaveSystem).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onQuit = typeof(SaveSystem).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onFocus);
            Assert.IsNotNull(onPause);
            Assert.IsNotNull(onQuit);

            int saveCount = 0;
            sys.Saved += _ => saveCount++;

            // Before Load(): even if dirty, focus loss / pause / quit must not call Save() or log Save blocked.
            sys.MarkDirty();
            onFocus.Invoke(sys, new object[] { false });
            onPause.Invoke(sys, new object[] { true });
            onQuit.Invoke(sys, null);
            Assert.AreEqual(0, saveCount);
            Assert.IsTrue(sys.IsDirty);

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.IsFalse(sys.IsDirty);

            // Clean state: lifecycle events must not save.
            onFocus.Invoke(sys, new object[] { false });
            onFocus.Invoke(sys, new object[] { true });
            onPause.Invoke(sys, new object[] { true });
            onPause.Invoke(sys, new object[] { false });
            onQuit.Invoke(sys, null);
            Assert.AreEqual(0, saveCount);

            // Focus gained (true) and unpause (false) must not save even when dirty.
            comp.Value = 42;
            sys.MarkDirty();
            onFocus.Invoke(sys, new object[] { true });
            onPause.Invoke(sys, new object[] { false });
            Assert.AreEqual(0, saveCount);
            Assert.IsTrue(sys.IsDirty);

            // Focus lost (false) saves when dirty and clears IsDirty.
            onFocus.Invoke(sys, new object[] { false });
            Assert.AreEqual(1, saveCount);
            Assert.IsFalse(sys.IsDirty);

            // Pause (true) saves when dirty.
            comp.Value = 84;
            sys.MarkDirty();
            onPause.Invoke(sys, new object[] { true });
            Assert.AreEqual(2, saveCount);
            Assert.IsFalse(sys.IsDirty);

            // Quit saves when dirty.
            comp.Value = 99;
            sys.MarkDirty();
            onQuit.Invoke(sys, null);
            Assert.AreEqual(3, saveCount);
            Assert.IsFalse(sys.IsDirty);

            comp.Value = 0;
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            Assert.AreEqual(99, comp.Value);
        }
    }
}
