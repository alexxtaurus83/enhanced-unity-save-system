using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [RequireComponent(typeof(SaveableEntity))]
    public class FruitContainer : MonoBehaviour, ISaveable
    {
        [SerializeField] private FruitType fruitType;

        private void Start()
        {
            PrintFruit();
        }

        private void PrintFruit()
        {
            if (fruitType == null)
            {
                Debug.Log($"{gameObject.name} --> no fruitType defined", this);
            }
            else
            {
                Debug.Log($"{gameObject.name} --> has fruitType: {fruitType}", this);
            }
        }

        [System.Serializable]
        private struct FruitContainerData
        {
            public string fruitId;
        }

        public bool NeedsReinstantiation() => true;

        public object SaveState()
        {
            return new FruitContainerData
            {
                fruitId = fruitType != null ? fruitType.GetID() : string.Empty
            };
        }

        public void LoadState(object state)
        {
            FruitContainerData data = SaveState.As<FruitContainerData>(state);
            fruitType = !string.IsNullOrEmpty(data.fruitId)
                ? IDFinder.GetScriptableObject<FruitType>(data.fruitId)
                : null;
            PrintFruit();
        }
    }
}
