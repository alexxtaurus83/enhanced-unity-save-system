# Mobile & Application Lifecycle

Mobile operating systems (iOS and Android) can suspend or terminate backgrounded apps without always firing `OnApplicationQuit`.

## Rules

- Save after meaningful gameplay milestones (level complete, purchase, upgrade), or call `SaveSystem.Instance.MarkDirty()` when state changes so `SaveSystem` saves automatically on `OnApplicationPause(true)`, `OnApplicationFocus(false)`, and `OnApplicationQuit()`. Never rely solely on `OnApplicationQuit`.
- `Save()` and `SaveAsync()` clear `IsDirty` on success; `SaveSystem` automatically saves if `IsDirty` (when loaded, not corrupt, and not busy) and calls `Flush()` in `OnApplicationPause(true)`, `OnApplicationFocus(false)`, and `OnApplicationQuit()`, and calls `Flush()` in `OnDestroy()` so queued background writes complete before process suspension.
- Store cooldowns and timers as UTC epoch timestamps (`endsAtUtc` in milliseconds) rather than remaining seconds, so elapsed offline time is computed accurately on load.
- Guard all save triggers with `SaveSystem.Instance.HasLoaded && !SaveSystem.Instance.IsCorrupt && !SaveSystem.Instance.IsBusy`.

## Example

```csharp
using System;
using FlexibleSave.Enhanced;
using UnityEngine;

public class UpgradeTimer : MonoBehaviour, ISaveable
{
    private long m_endsAtUtcMs;

    [Serializable]
    private struct TimerData
    {
        public long endsAtUtcMs;
    }

    public string SaveKey => "upgrade.timer";

    public void StartUpgrade(TimeSpan duration)
    {
        m_endsAtUtcMs = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeMilliseconds();
        SaveSystem.Instance.Save();
    }

    public double RemainingSeconds()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Math.Max(0.0, (m_endsAtUtcMs - now) / 1000.0);
    }

    public object SaveState() => new TimerData { endsAtUtcMs = m_endsAtUtcMs };

    public void LoadState(object state)
    {
        m_endsAtUtcMs = SaveState.As<TimerData>(state).endsAtUtcMs;
    }
}
```
