using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlexibleSave.Enhanced.Tests.Editor
{
    public class EntityRegistryTests
    {
        private readonly List<GameObject> m_objects = new List<GameObject>();

        private sealed class InitOnlySample
        {
            public readonly int ReadOnlyField = 99;
            [JsonProperty] private int m_privateWritable = 10;

            public int PrivateWritable => m_privateWritable;
        }

        [SetUp]
        public void SetUp()
        {
            SaveableEntityRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = m_objects.Count - 1; i >= 0; i--)
            {
                if (m_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_objects[i]);
                }
            }
            m_objects.Clear();
            SaveableEntityRegistry.Clear();
        }

        private SaveableEntity MakeEntity(string name, string id, bool manual)
        {
            var go = new GameObject(name);
            m_objects.Add(go);
            var e = go.AddComponent<SaveableEntity>();
            e.IsManualId = manual;
            e.AssignInternalId(id);
            return e;
        }

        [Test]
        public void TryRegister_AutoIdClone_ReassignsCloneOnly()
        {
            SaveableEntity original = MakeEntity("Original", "dup-guid", manual: false);
            SaveableEntity clone = MakeEntity("Clone", "dup-guid", manual: false);

            Assert.IsTrue(SaveableEntityRegistry.TryRegister(original));
            Assert.IsTrue(SaveableEntityRegistry.TryRegister(clone));

            Assert.AreEqual("dup-guid", original.ID);
            Assert.AreNotEqual("dup-guid", clone.ID);
            Assert.IsFalse(string.IsNullOrEmpty(clone.ID));
            Assert.AreSame(original, SaveableEntityRegistry.GetEntity(original.ID));
            Assert.AreSame(clone, SaveableEntityRegistry.GetEntity(clone.ID));
        }

        [Test]
        public void TryRegister_ManualDuplicate_LogsErrorAndRejectsSecond()
        {
            SaveableEntity first = MakeEntity("First", "player-singleton", manual: true);
            SaveableEntity second = MakeEntity("Second", "player-singleton", manual: true);

            Assert.IsTrue(SaveableEntityRegistry.TryRegister(first));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Duplicate manual ID 'player-singleton'"));
            Assert.IsFalse(SaveableEntityRegistry.TryRegister(second));

            Assert.AreSame(first, SaveableEntityRegistry.GetEntity("player-singleton"));

            // Destroying the rejected duplicate must not remove the original from registry.
            SaveableEntityRegistry.OnEntityDestroyed(second);
            Assert.AreSame(first, SaveableEntityRegistry.GetEntity("player-singleton"));
        }

        [Test]
        public void ChangeId_VerifiesOwnership_AndRejectsCollisions()
        {
            SaveableEntity a = MakeEntity("A", "id-a", manual: false);
            SaveableEntity b = MakeEntity("B", "id-b", manual: false);
            Assert.IsTrue(SaveableEntityRegistry.TryRegister(a));
            Assert.IsTrue(SaveableEntityRegistry.TryRegister(b));

            Assert.IsFalse(SaveableEntityRegistry.ChangeId(a, null));
            Assert.IsFalse(SaveableEntityRegistry.ChangeId(a, string.Empty));
            Assert.IsFalse(SaveableEntityRegistry.ChangeId(a, "id-b"));
            Assert.AreEqual("id-a", a.ID);
            Assert.AreSame(b, SaveableEntityRegistry.GetEntity("id-b"));

            Assert.IsTrue(SaveableEntityRegistry.ChangeId(a, "id-a-new"));
            Assert.IsNull(SaveableEntityRegistry.GetEntity("id-a"));
            Assert.AreSame(a, SaveableEntityRegistry.GetEntity("id-a-new"));
        }

        [Test]
        public void PrivateFieldContractResolver_SkipsInitOnly_SerializesPrivateWritable()
        {
            string json = "{\"ReadOnlyField\":123,\"m_privateWritable\":77}";
            var obj = JsonConvert.DeserializeObject<InitOnlySample>(json, SaveJson.CompactSettings);
            Assert.AreEqual(99, obj.ReadOnlyField);
            Assert.AreEqual(77, obj.PrivateWritable);
        }
    }
}
