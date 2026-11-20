using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    public class ActionCar : Vehicle
    {
        [SerializeField] private string specialEffect;

        [System.Serializable]
        private struct ActionCarData
        {
            public object vehicleData;
            public string specialEffect;
        }

        public override object SaveState()
        {
            return new ActionCarData
            {
                vehicleData = base.SaveState(),
                specialEffect = specialEffect
            };
        }

        public override void LoadState(object state)
        {
            ActionCarData data = SaveState.As<ActionCarData>(state);
            base.LoadState(data.vehicleData);
            specialEffect = data.specialEffect;
        }
    }
}
