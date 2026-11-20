# Getting Started

`SaveSystem` persists scene entities and prefabs to a single JSON (or encrypted binary) slot under `Application.persistentDataPath`.

## Rules

- Keep exactly one active `SaveSystem` component per loaded session (`[DisallowMultipleComponent]`). Duplicate instances log an error and destroy themselves.
- Always call `Load()` or `await LoadAsync()` before calling `Save()` or `SaveAsync()`. Saving before loading returns `SaveStatus.Blocked`.
- Add `SaveableEntity` to every `GameObject` whose `ISaveable` components should be persisted.
- Use `Tools/Flexible Save Enhanced/Create SaveablePrefabs Asset` if you spawn prefabs dynamically or use `NeedsReinstantiation() => true`.

## Example

```csharp
using FlexibleSave.Enhanced;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private async void Start()
    {
        LoadResult result = await SaveSystem.Instance.LoadAsync();
        if (result.Status == LoadStatus.Corrupt)
        {
            Debug.LogWarning($"Could not load save: {result.Error}");
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && SaveSystem.Instance != null && SaveSystem.Instance.HasLoaded && !SaveSystem.Instance.IsCorrupt)
        {
            SaveSystem.Instance.Save();
        }
    }
}
```
