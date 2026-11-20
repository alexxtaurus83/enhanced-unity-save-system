# Unity Compatibility (6.6, Fast Play Mode, IL2CPP)

Flexible Save Enhanced targets **Unity 6.6+** (`6000.6`) natively.

## Rules

- Static state on `SaveSystem`, `SaveableEntityRegistry`, `SaveablePrefabs`, and `AppSecretProvider` uses Unity 6.6 `[AutoStaticsCleanup]` (`Unity.Scripting.LifecycleManagement`) and `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` so **Enter Play Mode with Domain Reload Disabled** resets cleanly across repeated Play runs.
- `Runtime/link.xml` preserves `FlexibleSave.Enhanced.Runtime` types for IL2CPP managed code stripping.
- Always annotate your own private or nested save DTO structs/classes with `[Serializable]` and `[UnityEngine.Scripting.Preserve]` (or add your game assembly to your project's `link.xml`) so IL2CPP builds retain reflection metadata needed by Newtonsoft.Json.
- Avoid `Application.isEditor` or `Application.identifier` inside static field initializers or background threads; `SaveSystem.VerboseLogging` and `AppSecretProvider` initialize on the main thread.

## Example

```csharp
using System;
using UnityEngine.Scripting;

[Serializable, Preserve]
public struct Il2CppSafeSaveDto
{
    public int level;
    public long unlockedAtUtc;
}
```
