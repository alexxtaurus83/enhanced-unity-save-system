# Integrating MVC / MMVC Frameworks

Projects using MVC/MMVC or ScriptableObject/observable data models can integrate with `SaveSystem` without coupling the package to any external framework.

## Rules

- Keep the bridge `MonoBehaviour` inside your project assembly (outside the save package).
- Attach `SaveableEntity` with **Manual ID** enabled (for example `"progression"` or `"research"`) alongside a loader `MonoBehaviour` that implements `ISaveable`.
- Use a stable `SaveKey` on your loader `ISaveable` and convert your observable/model fields into a plain `JObject` or DTO (skipping `[NonSaved]` or transient view fields).
- Trigger `SaveSystem.Instance.LoadAsync()` as a startup pipeline step, and treat `LoadStatus.Corrupt` as a non-fatal warning that blocks saving while continuing with defaults.

## Example

```csharp
using System;
using FlexibleSave.Enhanced;
using Newtonsoft.Json.Linq;
using UnityEngine;

[RequireComponent(typeof(SaveableEntity))]
public class ModelSaveBridgeExample : MonoBehaviour, ISaveable
{
    [SerializeField] private string saveKey = "game.progress";

    private int m_currentLevel = 1;
    private int m_stars;

    public string SaveKey => saveKey;

    public object SaveState()
    {
        return new JObject
        {
            ["currentLevel"] = m_currentLevel,
            ["stars"] = m_stars
        };
    }

    public void LoadState(object state)
    {
        JObject obj = SaveState.As<JObject>(state);
        if (obj == null) return;
        m_currentLevel = obj.Value<int?>("currentLevel") ?? 1;
        m_stars = obj.Value<int?>("stars") ?? 0;
    }
}
```
