#define IS_SCRIPTABLE_OBJECT
//#define IS_MONO_BEHAVIOUR

using UnityEditor;
using UnityEngine;
using FlexibleSave.Enhanced.Editor;

namespace FSS_Demo
{
    [CustomEditor(typeof(FruitType))]
    public class FruitTypeEditor : SaveIDManagerEditor<FruitType>
    {
#if IS_SCRIPTABLE_OBJECT
        protected override FruitType[] GetObjects()
        {
            return Resources.LoadAll<FruitType>(string.Empty);
        }
#endif
#if IS_MONO_BEHAVIOUR
        protected override FruitType[] GetObjects()
        {
            return FindObjectsByType<FruitType>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
#endif

        protected override void OnEnable()
        {
            showBase = true;
            base.OnEnable();
        }
    }
}
