# Entities and IDs

Every persisted `GameObject` carries a `SaveableEntity` component that binds it to a unique string ID in `SaveableEntityRegistry`.

## Rules

- Use **Manual ID** (`IsManualId = true`) for stable scene singletons (for example `"player"` or `"level-progress"`). Never share a manual ID across two entities; duplicates fail validation in the Editor and are rejected at runtime.
- Leave **Manual ID** unchecked for dynamic or generic scene objects; the Editor assigns a GUID automatically and clones created via `Instantiate` or `Ctrl+D` receive a fresh GUID on the clone only.
- `SaveTransform` defaults to `false` for non-prefab entities. Enable it only when local position, rotation, and scale should be restored.
- Scene entities absent from the save file are **never** destroyed on load. Only IDs explicitly destroyed during gameplay (recorded in `deleted`) are destroyed on load.
- Child `SaveableEntity` components inside prefabs receive their `ChildID` (`m_childID`) at import time in the Editor; child GUIDs are never generated at runtime.

## Example

```csharp
using FlexibleSave.Enhanced;
using UnityEngine;

public class EntityLookupExample : MonoBehaviour
{
    public void MoveToPlayer()
    {
        SaveableEntity player = SaveableEntity.FindID("player");
        if (player != null)
        {
            transform.position = player.transform.position;
        }
    }
}
```
