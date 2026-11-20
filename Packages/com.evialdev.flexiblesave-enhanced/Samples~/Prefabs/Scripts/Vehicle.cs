using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [RequireComponent(typeof(SaveableEntity))]
    public class Vehicle : MonoBehaviour, ISaveable
    {
        [SerializeField] private string manufacturer;
        [SerializeField] private float speed;

        [System.Serializable]
        private struct VehicleData
        {
            public string manufacturer;
            public float speed;
        }

        public virtual bool NeedsReinstantiation() => true;

        public virtual object SaveState()
        {
            return new VehicleData
            {
                manufacturer = manufacturer,
                speed = speed
            };
        }

        public virtual void LoadState(object state)
        {
            VehicleData data = SaveState.As<VehicleData>(state);
            manufacturer = data.manufacturer;
            speed = data.speed;
        }
    }
}
