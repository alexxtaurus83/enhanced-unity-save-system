# Flexible Save Enhanced

A fast, modular JSON save system for **Unity 6.6+** (`6000.6`), packaged as a standard UPM package (`com.evialdev.flexiblesave-enhanced`).

## Features

- **Atomic writes & backup recovery:** Writes to `.tmp` and swaps via `File.Replace`, keeping a `.bak` fallback and copying corrupt files aside as `.corrupt.<utc>.json`.
- **Save guard:** Refuses to overwrite your save file before `Load()` completes or while a corrupt save is unresolved.
- **No `$type` metadata:** Plain JSON schema (`TypeNameHandling.None`) with explicit `SaveKey` / `Type.FullName` keys and `SaveState.As<T>(state)`.
- **Non-destructive scene loading:** Scene entities missing from the save file stay intact; only explicitly deleted IDs are destroyed.
- **Pluggable encryption:** Built-in `LightSaveEncryptor` (cached SHA-256 keys, AES-128-CBC + 16-byte HMAC-SHA256) or custom `ISaveEncryptor`.
- **Async & background writes:** Unity 6 `Awaitable` `LoadAsync` with monotonic `IProgress<float>`, optional single-writer background thread `Save()`, and WebGL `FS.syncfs` support.
- **Fast Play Mode ready:** Native Unity 6.6 `[AutoStaticsCleanup]` support with Domain Reload Disabled.

## Installation

### Git URL (Package Manager)

In Unity Package Manager (`Window > Package Manager > + > Add package from git URL...`):

```
https://github.com/alexxtaurus83/flexible-save-enhanced.git?path=Packages/com.evialdev.flexiblesave-enhanced
```

### Embedded Package

Copy `Packages/com.evialdev.flexiblesave-enhanced` into your project's `Packages/` folder.

## 5-Minute Quick Start

1. Add a `SaveSystem` component (`FlexibleSave.Enhanced.SaveSystem`) to a persistent GameObject in your startup scene.
2. Add `SaveableEntity` (`FlexibleSave.Enhanced.SaveableEntity`) to each GameObject you want to save. For scene singletons, enable **Manual ID** and set a stable ID (e.g. `"player"`).
3. Implement `ISaveable` on your behaviour:

```csharp
using System;
using FlexibleSave.Enhanced;
using UnityEngine;

[RequireComponent(typeof(SaveableEntity))]
public class PlayerHealth : MonoBehaviour, ISaveable
{
    [SerializeField] private int health = 100;

    [Serializable]
    private struct Data
    {
        public int health;
    }

    public string SaveKey => "player.health";
    public object SaveState() => new Data { health = health };
    public void LoadState(object state) => health = FlexibleSave.Enhanced.SaveState.As<Data>(state).health;
}
```

4. Load at startup before saving:

```csharp
LoadResult loadResult = await SaveSystem.Instance.LoadAsync();
if (loadResult.Status == LoadStatus.Corrupt)
{
    Debug.LogWarning($"Save corrupt: {loadResult.Error}");
}

// Later, save:
SaveSystem.Instance.Save();
```

## Documentation

Full guides are in `Packages/com.evialdev.flexiblesave-enhanced/Documentation~/`.

## Credits

Based on [Flexible Save System](https://github.com/KROIA/Flexible-save-system) by KROIA, MIT licensed. 2.0 is a rewrite and is not save- or API-compatible with the original.

## License

MIT — see [LICENSE.md](LICENSE.md).
