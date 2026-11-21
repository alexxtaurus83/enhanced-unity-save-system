using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlexibleSave.Enhanced.Tests.Runtime
{
    public class SaveSystemPlayModeTests
    {
        private string m_tempDir;
        private readonly List<UnityEngine.Object> m_cleanup = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            SaveableEntityRegistry.Clear();
            SaveablePrefabs.SetInstanceForTesting(null);
            m_tempDir = Path.Combine(Path.GetTempPath(), "FlexibleSavePlayTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            SaveablePrefabs.SetInstanceForTesting(null);
            for (int i = m_cleanup.Count - 1; i >= 0; i--)
            {
                if (m_cleanup[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_cleanup[i]);
                }
            }
            m_cleanup.Clear();
            SaveableEntityRegistry.Clear();

            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, recursive: true);
            }
        }

        private SaveSystem CreateSaveSystem(string fileName = "play_save.json")
        {
            var go = new GameObject("SaveSystem");
            m_cleanup.Add(go);
            var sys = go.AddComponent<SaveSystem>();
            sys.BasePath = m_tempDir;
            sys.Folder = string.Empty;
            sys.FileName = fileName;
            return sys;
        }

        private (SaveableEntity entity, TestSaveableBehaviour comp) CreateSceneEntity(string name, string manualId, bool saveTransform = false)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var entity = go.AddComponent<SaveableEntity>();
            entity.IsManualId = !string.IsNullOrEmpty(manualId);
            if (!string.IsNullOrEmpty(manualId))
            {
                entity.AssignInternalId(manualId);
            }
            entity.SaveTransform = saveTransform;
            var comp = go.AddComponent<TestSaveableBehaviour>();
            go.SetActive(true);
            m_cleanup.Add(go);
            return (entity, comp);
        }

        [UnityTest]
        public IEnumerator MissingSceneEntityStaysAlive_DeletedIdIsDestroyed()
        {
            SaveSystem sys = CreateSaveSystem();
            var (keepSaved, compSaved) = CreateSceneEntity("SavedEntity", "saved-1");
            compSaved.Counter = 11;

            var (toDelete, _) = CreateSceneEntity("DeletedEntity", "delete-me");

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);

            // Destroy 'toDelete' during gameplay, then save.
            UnityEngine.Object.Destroy(toDelete.gameObject);
            yield return null;

            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Recreate 'toDelete' and also add a brand-new scene entity NOT in the save file.
            var (recreatedDeleteTarget, _) = CreateSceneEntity("RecreatedTarget", "delete-me");
            var (untrackedSceneObj, _) = CreateSceneEntity("NewSceneEntity", "scene-only");

            compSaved.Counter = 0;
            LoadResult res = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, res.Status);

            Assert.AreEqual(11, compSaved.Counter);
            Assert.IsTrue(untrackedSceneObj != null, "Scene entities absent from save file must NOT be destroyed.");
            Assert.IsTrue(recreatedDeleteTarget == null, "Entity listed in 'deleted' must be destroyed on load.");
            Assert.IsTrue(compSaved.AllStateLoadedCalled);
            Assert.IsTrue(compSaved.DependenciesRestoredCalled);
        }

        [UnityTest]
        public IEnumerator InactiveEntity_IsSavedAndRestored()
        {
            SaveSystem sys = CreateSaveSystem();
            var (entity, comp) = CreateSceneEntity("InactiveEntity", "inactive-1");
            comp.Counter = 77;
            comp.Note = "dormant";

            // Disable after Awake (Awake->OnDestroy tracking keeps it registered).
            entity.gameObject.SetActive(false);
            yield return null;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Counter = 0;
            comp.Note = "dirty";

            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);
            Assert.AreEqual(77, comp.Counter);
            Assert.AreEqual("dormant", comp.Note);
        }

        [UnityTest]
        public IEnumerator ParentPath_AndLocalTransform_AreRestored()
        {
            SaveSystem sys = CreateSaveSystem();
            var (parentEntity, parentComp) = CreateSceneEntity("ParentRoot", "parent-root");
            var container = new GameObject("Container");
            container.transform.SetParent(parentEntity.transform, false);
            var socket = new GameObject("Socket");
            socket.transform.SetParent(container.transform, false);
            m_cleanup.Add(container);

            var (childEntity, childComp) = CreateSceneEntity("ChildObj", "child-obj", saveTransform: true);
            childEntity.transform.SetParent(socket.transform, false);
            childEntity.transform.localPosition = new Vector3(1.5f, 2.5f, -3.5f);
            childEntity.transform.localScale = new Vector3(2f, 2f, 2f);
            childComp.Counter = 88;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Detach child and scramble its transform before loading.
            childEntity.transform.SetParent(null, false);
            childEntity.transform.localPosition = Vector3.zero;
            childEntity.transform.localScale = Vector3.one;
            childComp.Counter = 0;
            yield return null;

            LoadResult res = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, res.Status);
            Assert.AreEqual(88, childComp.Counter);
            Assert.AreSame(socket.transform, childEntity.transform.parent);
            Assert.AreEqual(new Vector3(1.5f, 2.5f, -3.5f), childEntity.transform.localPosition);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), childEntity.transform.localScale);
            Assert.AreSame(childEntity.gameObject, parentComp.LastAddedChild);
            Assert.AreSame(socket, parentComp.LastAddedChildParent);
        }

        [UnityTest]
        public IEnumerator PrefabReinstantiation_PreservesDeletedChildren()
        {
            SaveSystem sys = CreateSaveSystem();

            // Build a prefab template (HideAndDontSave so it behaves like an asset, not a scene entity).
            var prefabRoot = new GameObject("TankPrefab");
            prefabRoot.hideFlags = HideFlags.HideAndDontSave;
            prefabRoot.SetActive(false);
            m_cleanup.Add(prefabRoot);

            var rootEntity = prefabRoot.AddComponent<SaveableEntity>();
            SetPrivateField(rootEntity, "m_prefabID", "tank-prefab");
            var rootComp = prefabRoot.AddComponent<TestSaveableBehaviour>();
            rootComp.ShouldReinstantiate = true;

            var childA = new GameObject("TurretA");
            childA.transform.SetParent(prefabRoot.transform, false);
            var childEntityA = childA.AddComponent<SaveableEntity>();
            SetPrivateField(childEntityA, "m_childID", "child-a");
            childA.AddComponent<TestSaveableBehaviour>();

            var childB = new GameObject("TurretB");
            childB.transform.SetParent(prefabRoot.transform, false);
            var childEntityB = childB.AddComponent<SaveableEntity>();
            SetPrivateField(childEntityB, "m_childID", "child-b");
            childB.AddComponent<TestSaveableBehaviour>();

            var prefabsAsset = ScriptableObject.CreateInstance<SaveablePrefabs>();
            m_cleanup.Add(prefabsAsset);
            prefabsAsset.Prefabs.Add(rootEntity);
            prefabsAsset.InvalidateLookup();
            SaveablePrefabs.SetInstanceForTesting(prefabsAsset);

            // Spawn instance into scene
            GameObject instance = UnityEngine.Object.Instantiate(prefabRoot);
            instance.SetActive(true);
            m_cleanup.Add(instance);
            yield return null;

            var instEntity = instance.GetComponent<SaveableEntity>();
            instEntity.GetComponent<TestSaveableBehaviour>().Counter = 321;
            string savedRootId = instEntity.ID;

            // Destroy TurretB before saving
            Transform turretBTransform = instance.transform.Find("TurretB");
            Assert.IsNotNull(turretBTransform);
            UnityEngine.Object.Destroy(turretBTransform.gameObject);
            yield return null;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Destroy entire instance, then Load -> should reinstantiate and keep TurretB destroyed!
            UnityEngine.Object.Destroy(instance);
            yield return null;
            // Clear pending deleted ID for savedRootId as if starting a fresh session loading the save:
            SaveableEntityRegistry.ClearPendingDeletedIds();

            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);

            SaveableEntity restored = SaveableEntityRegistry.GetEntity(savedRootId);
            Assert.IsNotNull(restored);
            m_cleanup.Add(restored.gameObject);
            Assert.AreEqual(321, restored.GetComponent<TestSaveableBehaviour>().Counter);
            Assert.IsNotNull(restored.transform.Find("TurretA"));
            Assert.IsNull(restored.transform.Find("TurretB"));

            // Save again after load and reload once more -> TurretB must STILL remain deleted!
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            LoadResult secondLoad = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, secondLoad.Status);
            SaveableEntity restoredAgain = SaveableEntityRegistry.GetEntity(savedRootId);
            Assert.IsNotNull(restoredAgain);
            m_cleanup.Add(restoredAgain.gameObject);
            Assert.IsNotNull(restoredAgain.transform.Find("TurretA"));
            Assert.IsNull(restoredAgain.transform.Find("TurretB"));
        }

        private sealed class SyncProgress : IProgress<float>
        {
            public readonly List<float> Values = new List<float>();
            public void Report(float value) => Values.Add(value);
        }

        [UnityTest]
        public IEnumerator LoadAsync_ReportsMonotonicProgress_AndRejectsReentry()
        {
            SaveSystem sys = CreateSaveSystem();
            for (int i = 0; i < 5; i++)
            {
                var (_, c) = CreateSceneEntity("E" + i, "e-" + i);
                c.Counter = i + 10;
            }
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            var progress = new SyncProgress();
            Awaitable<LoadResult> firstTask = sys.LoadAsync(progress);

            // Immediate re-entry while LoadAsync is running must throw InvalidOperationException.
            Assert.IsTrue(sys.IsBusy);
            Assert.Throws<InvalidOperationException>(() => sys.LoadAsync());

            var awaiter = firstTask.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            LoadResult result = awaiter.GetResult();
            Assert.AreEqual(LoadStatus.Ok, result.Status);
            Assert.IsFalse(sys.IsBusy);
            Assert.GreaterOrEqual(progress.Values.Count, 3);
            Assert.AreEqual(1f, progress.Values[progress.Values.Count - 1], 0.001f);

            for (int i = 1; i < progress.Values.Count; i++)
            {
                Assert.GreaterOrEqual(progress.Values[i], progress.Values[i - 1], "Progress must be monotonic.");
            }
        }

        [UnityTest]
        public IEnumerator BackgroundWrite_AndFlush_PersistLatestSnapshot()
        {
            SaveSystem sys = CreateSaveSystem();
            sys.UseBackgroundWrite = true;
            var (_, comp) = CreateSceneEntity("Hero", "hero-bg");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);

            comp.Counter = 100;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            comp.Counter = 200;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            sys.Flush(timeoutMs: 2000);
            yield return null;

            comp.Counter = 0;
            LoadResult loaded = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loaded.Status);
            Assert.AreEqual(200, comp.Counter);
        }

        [UnityTest]
        public IEnumerator PlayTwiceWithoutDomainReload_ResetsStaticsAndKeepsSameIds()
        {
            // Run 1
            SaveSystem sys1 = CreateSaveSystem();
            var (e1, c1) = CreateSceneEntity("PersistentHero", "hero-singleton");
            c1.Counter = 55;
            Assert.AreSame(sys1, SaveSystem.Instance);
            Assert.AreEqual(LoadStatus.NoSaveFile, sys1.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys1.Save().Status);

            // Simulate exiting Play Mode without domain reload: destroy objects & clear statics.
            UnityEngine.Object.DestroyImmediate(e1.gameObject);
            UnityEngine.Object.DestroyImmediate(sys1.gameObject);
            SaveableEntityRegistry.Clear();
            yield return null;

            Assert.IsNull(SaveSystem.Instance);
            Assert.AreEqual(0, SaveableEntityRegistry.Count);

            // Run 2
            SaveSystem sys2 = CreateSaveSystem();
            var (e2, c2) = CreateSceneEntity("PersistentHero", "hero-singleton");
            Assert.AreSame(sys2, SaveSystem.Instance);
            Assert.AreEqual(1, SaveableEntityRegistry.Count);
            Assert.AreEqual(LoadStatus.Ok, sys2.Load().Status);
            Assert.AreEqual(55, c2.Counter);
            Assert.AreEqual("hero-singleton", e2.ID);
        }

        [UnityTest]
        public IEnumerator RootEntityReparentedAsChild_RemovesStaleRootEntry()
        {
            SaveSystem sys = CreateSaveSystem();
            var (parent, _) = CreateSceneEntity("Parent", "parent-reparent");
            var (child, childComp) = CreateSceneEntity("Child", "child-reparent");
            childComp.Counter = 10;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Reparent child under parent and re-save.
            child.transform.SetParent(parent.transform, false);
            childComp.Counter = 25;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            JObject doc = JObject.Parse(File.ReadAllText(sys.FullPath));
            var entities = (JObject)doc["entities"];
            Assert.IsNotNull(entities["parent-reparent"]);
            Assert.IsNull(entities["child-reparent"], "Stale root entry must be removed when entity becomes a child.");
            Assert.AreEqual("child-reparent", entities["parent-reparent"]?["children"]?[0]?["meta"]?["id"]?.ToString());

            childComp.Counter = 0;
            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);
            Assert.AreEqual(1, loadRes.Restored);
            Assert.AreEqual(25, childComp.Counter);
            Assert.AreSame(parent.transform, child.transform.parent);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RootEntityReparentedUnderOtherEntity_IsDetachedOnLoad()
        {
            SaveSystem sys = CreateSaveSystem();
            var (otherParent, _) = CreateSceneEntity("OtherParent", "other-parent");
            var (rootEntity, rootComp) = CreateSceneEntity("RootItem", "root-item", saveTransform: true);
            rootEntity.transform.localPosition = new Vector3(3f, 4f, 5f);
            rootComp.Counter = 77;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Reparent root entity under otherParent and move it before loading.
            rootEntity.transform.SetParent(otherParent.transform, false);
            rootEntity.transform.localPosition = new Vector3(99f, 99f, 99f);
            rootComp.Counter = 0;
            yield return null;

            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);
            Assert.IsNull(rootEntity.transform.parent, "Root entity reparented under another SaveableEntity must be detached on load.");
            Assert.AreEqual(new Vector3(3f, 4f, 5f), rootEntity.transform.localPosition);
            Assert.AreEqual(77, rootComp.Counter);
        }

        [UnityTest]
        public IEnumerator ReinstantiateParentPrefab_PreservesNonPrefabSceneChild()
        {
            SaveSystem sys = CreateSaveSystem();

            var prefabRoot = new GameObject("PlayerPrefab");
            prefabRoot.hideFlags = HideFlags.HideAndDontSave;
            prefabRoot.SetActive(false);
            m_cleanup.Add(prefabRoot);

            var prefabEntity = prefabRoot.AddComponent<SaveableEntity>();
            SetPrivateField(prefabEntity, "m_prefabID", "player-prefab");
            var prefabComp = prefabRoot.AddComponent<TestSaveableBehaviour>();
            prefabComp.ShouldReinstantiate = true;

            var prefabsAsset = ScriptableObject.CreateInstance<SaveablePrefabs>();
            m_cleanup.Add(prefabsAsset);
            prefabsAsset.Prefabs.Add(prefabEntity);
            prefabsAsset.InvalidateLookup();
            SaveablePrefabs.SetInstanceForTesting(prefabsAsset);

            GameObject playerInst = UnityEngine.Object.Instantiate(prefabRoot);
            playerInst.SetActive(true);
            m_cleanup.Add(playerInst);
            yield return null;

            var playerEntity = playerInst.GetComponent<SaveableEntity>();
            playerEntity.GetComponent<TestSaveableBehaviour>().Counter = 50;
            string savedPlayerId = playerEntity.ID;

            var (sceneItem, sceneItemComp) = CreateSceneEntity("EquippedSceneSword", "scene-sword", saveTransform: true);
            sceneItem.transform.SetParent(playerInst.transform, false);
            sceneItem.transform.localPosition = new Vector3(0.5f, 1f, 0f);
            sceneItemComp.Counter = 999;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            sceneItemComp.Counter = 0;
            LoadResult loadRes = sys.Load();
            Assert.AreEqual(LoadStatus.Ok, loadRes.Status);

            SaveableEntity restoredPlayer = SaveableEntityRegistry.GetEntity(savedPlayerId);
            Assert.IsNotNull(restoredPlayer);
            m_cleanup.Add(restoredPlayer.gameObject);
            Assert.AreEqual(50, restoredPlayer.GetComponent<TestSaveableBehaviour>().Counter);
            Assert.IsTrue(sceneItem != null, "Non-prefab scene child must survive parent prefab reinstantiation.");
            Assert.AreSame(restoredPlayer.transform, sceneItem.transform.parent);
            Assert.AreEqual(999, sceneItemComp.Counter);
            Assert.AreEqual(new Vector3(0.5f, 1f, 0f), sceneItem.transform.localPosition);
        }

        [UnityTest]
        public IEnumerator NestedPrefabChildDeletion_IsPreservedAcrossReload()
        {
            SaveSystem sys = CreateSaveSystem();

            var prefabRoot = new GameObject("RootVehicle");
            prefabRoot.hideFlags = HideFlags.HideAndDontSave;
            prefabRoot.SetActive(false);
            m_cleanup.Add(prefabRoot);

            var rootEntity = prefabRoot.AddComponent<SaveableEntity>();
            SetPrivateField(rootEntity, "m_prefabID", "nested-vehicle");
            var rootComp = prefabRoot.AddComponent<TestSaveableBehaviour>();
            rootComp.ShouldReinstantiate = true;

            var midNode = new GameObject("Turret");
            midNode.transform.SetParent(prefabRoot.transform, false);
            var midEntity = midNode.AddComponent<SaveableEntity>();
            SetPrivateField(midEntity, "m_childID", "turret-child");
            midNode.AddComponent<TestSaveableBehaviour>();

            var grandChild = new GameObject("Barrel");
            grandChild.transform.SetParent(midNode.transform, false);
            var grandEntity = grandChild.AddComponent<SaveableEntity>();
            SetPrivateField(grandEntity, "m_childID", "barrel-grandchild");
            grandChild.AddComponent<TestSaveableBehaviour>();

            var prefabsAsset = ScriptableObject.CreateInstance<SaveablePrefabs>();
            m_cleanup.Add(prefabsAsset);
            prefabsAsset.Prefabs.Add(rootEntity);
            prefabsAsset.InvalidateLookup();
            SaveablePrefabs.SetInstanceForTesting(prefabsAsset);

            GameObject instance = UnityEngine.Object.Instantiate(prefabRoot);
            instance.SetActive(true);
            m_cleanup.Add(instance);
            yield return null;

            string rootId = instance.GetComponent<SaveableEntity>().ID;
            Transform barrelTransform = instance.transform.Find("Turret/Barrel");
            Assert.IsNotNull(barrelTransform);
            UnityEngine.Object.Destroy(barrelTransform.gameObject);
            yield return null;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            UnityEngine.Object.Destroy(instance);
            yield return null;
            SaveableEntityRegistry.ClearPendingDeletedIds();

            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            SaveableEntity restored = SaveableEntityRegistry.GetEntity(rootId);
            Assert.IsNotNull(restored);
            m_cleanup.Add(restored.gameObject);
            Assert.IsNotNull(restored.transform.Find("Turret"));
            Assert.IsNull(restored.transform.Find("Turret/Barrel"), "Deleted grandchild inside intermediate prefab child must remain deleted.");

            // Save and reload a second time to verify intermediate node kept m_knownChildIds.
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            SaveableEntity restoredAgain = SaveableEntityRegistry.GetEntity(rootId);
            Assert.IsNotNull(restoredAgain);
            m_cleanup.Add(restoredAgain.gameObject);
            Assert.IsNull(restoredAgain.transform.Find("Turret/Barrel"));
        }

        [UnityTest]
        public IEnumerator NeverActivatedInactiveSceneEntity_IsSavedAfterNoSaveFile()
        {
            SaveSystem sys = CreateSaveSystem();

            var go = new GameObject("NeverActivated");
            go.SetActive(false);
            m_cleanup.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            entity.IsManualId = true;
            entity.AssignInternalId("never-active-1");
            var comp = go.AddComponent<TestSaveableBehaviour>();
            comp.Counter = 314;

            Assert.AreEqual(0, SaveableEntityRegistry.Count, "Never-activated entity has not run Awake yet.");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(1, SaveableEntityRegistry.Count, "NoSaveFile load must register inactive scene entities.");
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Counter = 0;
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            Assert.AreEqual(314, comp.Counter);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveSystemOnDestroy_DoesNotClearSurvivingEntities()
        {
            SaveSystem sys = CreateSaveSystem();
            var (entity, _) = CreateSceneEntity("SurvivingEntity", "surviving-1");
            Assert.AreEqual(1, SaveableEntityRegistry.Count);

            UnityEngine.Object.Destroy(sys.gameObject);
            yield return null;

            Assert.AreEqual(1, SaveableEntityRegistry.Count);
            Assert.AreSame(entity, SaveableEntityRegistry.GetEntity("surviving-1"));
        }

        private sealed class BlockingFirstWriteFileSystem : IFileSystem
        {
            private readonly IFileSystem m_inner = PhysicalFileSystem.Instance;
            private readonly System.Threading.ManualResetEventSlim m_firstWriteStarted = new System.Threading.ManualResetEventSlim(false);
            private readonly System.Threading.ManualResetEventSlim m_allowFirstWrite = new System.Threading.ManualResetEventSlim(false);
            private int m_writeCount;

            public bool Exists(string path) => m_inner.Exists(path);
            public void CreateDirectory(string path) => m_inner.CreateDirectory(path);
            public byte[] ReadAllBytes(string path) => m_inner.ReadAllBytes(path);
            public void Copy(string s, string d, bool o) => m_inner.Copy(s, d, o);
            public void Delete(string path) => m_inner.Delete(path);
            public void Move(string s, string d) => m_inner.Move(s, d);
            public void Replace(string s, string d, string b) => m_inner.Replace(s, d, b);

            public void WriteAllBytes(string path, byte[] bytes)
            {
                if (System.Threading.Interlocked.Increment(ref m_writeCount) == 1)
                {
                    m_firstWriteStarted.Set();
                    m_allowFirstWrite.Wait(5000);
                }
                m_inner.WriteAllBytes(path, bytes);
            }

            public void WaitForFirstWriteStart() => m_firstWriteStarted.Wait(5000);
            public void ReleaseFirstWrite() => m_allowFirstWrite.Set();
        }

        [UnityTest]
        public IEnumerator BackgroundSave_And_SaveAsync_Coalesce_NotifyOnce()
        {
            SaveSystem sys = CreateSaveSystem();
            sys.UseBackgroundWrite = true;
            var (_, comp) = CreateSceneEntity("CoalesceHero", "coalesce-1");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);

            var blockingFs = new BlockingFirstWriteFileSystem();
            sys.FileSystem = blockingFs;

            int savedCount = 0;
            sys.Saved += _ => savedCount++;

            // 1st Save occupies the background writer inside WriteAllBytes.
            comp.Counter = 100;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            blockingFs.WaitForFirstWriteStart();

            // 2nd Save + SaveAsync coalesce onto the same pending batch while writer is busy.
            comp.Counter = 111;
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Counter = 222;
            Awaitable<SaveResult> asyncTask = sys.SaveAsync();

            blockingFs.ReleaseFirstWrite();

            var awaiter = asyncTask.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            SaveResult asyncResult = awaiter.GetResult();
            Assert.AreEqual(SaveStatus.Ok, asyncResult.Status);
            sys.Flush(timeoutMs: 2000);
            yield return null;

            Assert.AreEqual(2, savedCount, "First write (1) + coalesced Save+SaveAsync batch (1) must notify Saved twice total.");
            comp.Counter = 0;
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            Assert.AreEqual(222, comp.Counter);
        }

        [UnityTest]
        public IEnumerator LoadAsync_DestroyedMidFlight_DoesNotRestoreOrInvokeLoaded()
        {
            SaveSystem sys = CreateSaveSystem();
            var (_, comp) = CreateSceneEntity("Hero", "destroy-load-async");
            comp.Counter = 55;
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            comp.Counter = 10;
            bool loadedCalled = false;
            sys.Loaded += _ => loadedCalled = true;

            Awaitable<LoadResult> task = sys.LoadAsync();
            UnityEngine.Object.Destroy(sys.gameObject);

            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            _ = awaiter.GetResult();
            Assert.IsFalse(loadedCalled, "Destroyed SaveSystem must not invoke Loaded.");
            Assert.AreEqual(10, comp.Counter, "Destroyed SaveSystem must not mutate scene state.");
        }

        [UnityTest]
        public IEnumerator IDFinder_GetGameObject_CachesAndFindsSpawnedObjects()
        {
            var (first, _) = CreateSceneEntity("First", "finder-1");
            Assert.AreSame(first, IDFinder.GetGameObject<SaveableEntity>("finder-1"));
            Assert.IsNull(IDFinder.GetGameObject<SaveableEntity>("finder-2"));

            yield return null;

            var (second, _) = CreateSceneEntity("Second", "finder-2");
            Assert.AreSame(second, IDFinder.GetGameObject<SaveableEntity>("finder-2"));
        }

        [UnityTest]
        public IEnumerator BackgroundWrite_MarkDirtyDuringInFlightSave_PreservesDirtyAndSavesOnFocusLoss()
        {
            SaveSystem sys = CreateSaveSystem();
            sys.UseBackgroundWrite = true;
            var (_, comp) = CreateSceneEntity("Hero", "bg-dirty");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);

            var blockingFs = new BlockingFirstWriteFileSystem();
            sys.FileSystem = blockingFs;

            comp.Counter = 10;
            sys.MarkDirty();
            Assert.IsTrue(sys.IsDirty);

            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            Assert.IsFalse(sys.IsDirty, "Save() should clear IsDirty once snapshot is queued.");
            blockingFs.WaitForFirstWriteStart();

            // Mutate and mark dirty while first background write is in flight.
            comp.Counter = 20;
            sys.MarkDirty();
            Assert.IsTrue(sys.IsDirty);

            blockingFs.ReleaseFirstWrite();
            sys.Flush(timeoutMs: 2000);
            yield return null;

            Assert.IsTrue(sys.IsDirty, "Completing an earlier background write must not clear a newer MarkDirty().");

            // Focus loss should save the newer dirty snapshot and flush it.
            MethodInfo onFocus = typeof(SaveSystem).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onFocus);
            onFocus.Invoke(sys, new object[] { false });
            yield return null;

            Assert.IsFalse(sys.IsDirty);
            comp.Counter = 0;
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);
            Assert.AreEqual(20, comp.Counter);
        }

        [UnityTest]
        public IEnumerator SaveAsync_ClearsIsDirty_AndReMarksOnFailure()
        {
            SaveSystem sys = CreateSaveSystem();
            sys.UseBackgroundWrite = true;
            var (_, comp) = CreateSceneEntity("Hero", "async-dirty");
            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);

            comp.Counter = 50;
            sys.MarkDirty();
            Awaitable<SaveResult> okTask = sys.SaveAsync();
            var okAwaiter = okTask.GetAwaiter();
            while (!okAwaiter.IsCompleted)
            {
                yield return null;
            }
            Assert.AreEqual(SaveStatus.Ok, okAwaiter.GetResult().Status);
            Assert.IsFalse(sys.IsDirty);

            var failingFs = new FailingWriteFileSystem();
            sys.FileSystem = failingFs;
            comp.Counter = 60;
            sys.MarkDirty();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Save failed"));
            Awaitable<SaveResult> failTask = sys.SaveAsync();
            var failAwaiter = failTask.GetAwaiter();
            while (!failAwaiter.IsCompleted)
            {
                yield return null;
            }
            Assert.AreEqual(SaveStatus.Failed, failAwaiter.GetResult().Status);
            Assert.IsTrue(sys.IsDirty, "Failed background SaveAsync must restore IsDirty.");
        }

        private sealed class FailingWriteFileSystem : IFileSystem
        {
            private readonly IFileSystem m_inner = PhysicalFileSystem.Instance;
            public bool Exists(string path) => m_inner.Exists(path);
            public void CreateDirectory(string path) => m_inner.CreateDirectory(path);
            public byte[] ReadAllBytes(string path) => m_inner.ReadAllBytes(path);
            public void Copy(string s, string d, bool o) => m_inner.Copy(s, d, o);
            public void Delete(string path) => m_inner.Delete(path);
            public void Move(string s, string d) => m_inner.Move(s, d);
            public void Replace(string s, string d, string b) => m_inner.Replace(s, d, b);
            public void WriteAllBytes(string path, byte[] bytes) => throw new IOException("Simulated background write failure.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"Field '{fieldName}' not found on {target.GetType().Name}");
            fi.SetValue(target, value);
        }
    }
}
