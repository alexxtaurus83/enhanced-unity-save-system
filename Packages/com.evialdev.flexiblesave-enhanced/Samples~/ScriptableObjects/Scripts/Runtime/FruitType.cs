using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [CreateAssetMenu(fileName = "FruitType", menuName = "ScriptableObjects/FruitType")]
    public class FruitType : ScriptableObject, ISaveID
    {
        [SerializeField] [HideInInspector] private string m_ID;

        public string GetID() => m_ID;

        public void SetID(string id) => m_ID = id;

        private enum Taste
        {
            sweet,
            sour,
        }

        [SerializeField] private int weight;
        [SerializeField] private Taste taste;
        [SerializeField] private string description;

        public override string ToString()
        {
            return $"{description} Taste: {taste} Weight: {weight}";
        }
    }
}
