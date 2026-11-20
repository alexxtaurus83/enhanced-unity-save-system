# Prefabs and Children

`SaveablePrefabs` maps `PrefabID` strings to prefab assets so runtime-spawned objects or reinstantiated entities can be recreated on load.

## Rules

- Create the registry asset once via `Tools/Flexible Save Enhanced/Create SaveablePrefabs Asset`. It must live at `Assets/Resources/FlexibleSave/SaveablePrefabs.asset`.
- Do not duplicate prefab assets without regenerating their `PrefabID`; the Editor reports duplicate `PrefabID` conflicts in `SaveablePrefabs` and on Play entry.
- Child `SaveableEntity` objects inside a prefab receive persistent `m_childID` values during Editor prefab post-processing. If a child is destroyed during gameplay, its `childId` is recorded in `deletedChildren` so reinstantiation removes it again.
- Parent-child relationships between `SaveableEntity` instances store a relative `parentPath` from the parent entity to `transform.parent` (omitted when direct). Parenting runs with `worldPositionStays: false` before `ResetTransform()`.

## Example

```csharp
using FlexibleSave.Enhanced;
using UnityEngine;

public class SpawnerExample : MonoBehaviour, ISaveable
{
    public bool NeedsReinstantiation() => true;

    public object SaveState() => null;

    public void LoadState(object state) { }

    public void GotAddedAsChild(GameObject obj, GameObject hisParent)
    {
        Debug.Log($"Loaded child '{obj.name}' attached under '{hisParent.name}'.");
    }
}
```
