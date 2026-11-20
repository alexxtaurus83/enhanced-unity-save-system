using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [RequireComponent(typeof(SaveableEntity))]
    public class Sphere : MonoBehaviour, ISaveable
    {
        [SerializeField] private float speed;
        private float m_timePos;

        private void Update()
        {
            m_timePos += Time.deltaTime * speed;
            float scale = 1.3f + 0.5f * Mathf.Sin(m_timePos);
            transform.localScale = new Vector3(scale, scale, scale);
        }

        [System.Serializable]
        private struct SphereData
        {
            public float speed;
            public float timePos;
        }

        public bool NeedsReinstantiation() => true;

        public object SaveState()
        {
            return new SphereData
            {
                speed = speed,
                timePos = m_timePos
            };
        }

        public void LoadState(object state)
        {
            SphereData data = SaveState.As<SphereData>(state);
            speed = data.speed;
            m_timePos = data.timePos;
        }
    }
}
