using UnityEngine;
using FlexibleSave.Enhanced;

namespace FSS_Demo
{
    [RequireComponent(typeof(SaveableEntity))]
    public class Block : MonoBehaviour, ISaveable
    {
        [SerializeField] private float speed;
        [SerializeField] private Vector3 direction;
        [SerializeField] private GameObject movingPart;

        private float m_timePos;

        private void Start()
        {
            if (TryGetComponent<SaveableEntity>(out SaveableEntity entity))
            {
                entity.SaveTransform = false;
            }
        }

        private void Update()
        {
            if (movingPart == null)
            {
                return;
            }

            m_timePos += Time.deltaTime * speed;
            movingPart.transform.localPosition = Mathf.Sin(m_timePos) * direction;
        }

        [System.Serializable]
        private struct BlockData
        {
            public float speed;
            public Vector3Data direction;
            public float timePos;
        }

        public bool NeedsReinstantiation() => true;

        public object SaveState()
        {
            return new BlockData
            {
                speed = speed,
                direction = new Vector3Data(direction),
                timePos = m_timePos
            };
        }

        public void LoadState(object state)
        {
            BlockData data = SaveState.As<BlockData>(state);
            speed = data.speed;
            direction = data.direction.ToVector3();
            m_timePos = data.timePos;
        }
    }
}
