using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [RequireComponent(typeof(SaveableEntity))]
    public class Player : MonoBehaviour, ISaveable
    {
        [SerializeField] private int age;
        [SerializeField] private string language;

        [System.Serializable]
        private struct PlayerData
        {
            public int age;
            public string language;
        }

        public bool NeedsReinstantiation() => true;

        public object SaveState()
        {
            return new PlayerData
            {
                age = age,
                language = language
            };
        }

        public void LoadState(object state)
        {
            PlayerData data = SaveState.As<PlayerData>(state);
            age = data.age;
            language = data.language;
        }

        public void AllObjectsStateLoaded(object state)
        {
            // All saved entities now exist and have raw data restored.
            // Use SaveableEntity.FindID(id) here to resolve cross-entity references.
        }
    }
}
