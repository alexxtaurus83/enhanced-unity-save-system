using System;
using UnityEngine;

namespace FlexibleSave.Enhanced.Tests.Runtime
{
    public class TestSaveableBehaviour : MonoBehaviour, ISaveable
    {
        public string CustomKey;
        public bool ShouldSave = true;
        public bool ShouldReinstantiate;
        public int Counter;
        public string Note;
        public bool AllStateLoadedCalled { get; private set; }
        public bool DependenciesRestoredCalled { get; private set; }
        public GameObject LastAddedChild { get; private set; }
        public GameObject LastAddedChildParent { get; private set; }

        [Serializable]
        public struct Payload
        {
            public int counter;
            public string note;
        }

        public string SaveKey => CustomKey;
        public bool NeedsToBeSaved() => ShouldSave;
        public bool NeedsReinstantiation() => ShouldReinstantiate;

        public object SaveState()
        {
            return new Payload { counter = Counter, note = Note };
        }

        public void LoadState(object state)
        {
            Payload payload = Enhanced.SaveState.As<Payload>(state);
            Counter = payload.counter;
            Note = payload.note;
        }

        public void AllObjectsStateLoaded(object state)
        {
            AllStateLoadedCalled = true;
        }

        public void AllObjectsDependenciesRestored()
        {
            DependenciesRestoredCalled = true;
        }

        public void GotAddedAsChild(GameObject obj, GameObject hisParent)
        {
            LastAddedChild = obj;
            LastAddedChildParent = hisParent;
        }
    }
}
