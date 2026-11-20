# ISaveable Lifecycle

`ISaveable` uses C# default interface members so components only implement the methods they need.

## Rules

- Override `SaveKey` with a stable string (for example `"inventory"`) whenever refactoring class names or namespaces might otherwise break saved component keys. When `SaveKey` is `null`, `GetType().FullName` is used.
- Return plain serializable DTO structs or classes from `SaveState()`. **Never** return `UnityEngine.Object` references (such as `GameObject`, `Transform`, or `ScriptableObject`); save string IDs (`ISaveID.GetID()`) instead.
- Convert the `state` argument in `LoadState(object state)` and `AllObjectsStateLoaded(object state)` with `FlexibleSave.Enhanced.SaveState.As<T>(state)` — at runtime `state` is a `Newtonsoft.Json.Linq.JToken`.
- Annotate custom DTOs/types with `[UnityEngine.Scripting.Preserve]` (or `[Serializable]`) so IL2CPP code stripping does not remove constructors or fields.
- Use the three-phase restore order cleanly: `LoadState` restores raw values; `AllObjectsStateLoaded` resolves references to other entities; `AllObjectsDependenciesRestored` runs post-restore logic.

## Example

```csharp
using System;
using FlexibleSave.Enhanced;
using UnityEngine;
using UnityEngine.Scripting;

[RequireComponent(typeof(SaveableEntity))]
public class Wallet : MonoBehaviour, ISaveable
{
    [SerializeField] private int coins;

    [Serializable, Preserve]
    private struct WalletData
    {
        public int coins;
    }

    public string SaveKey => "player.wallet";

    public object SaveState() => new WalletData { coins = coins };

    public void LoadState(object state)
    {
        WalletData data = SaveState.As<WalletData>(state);
        coins = data.coins;
    }
}
```
