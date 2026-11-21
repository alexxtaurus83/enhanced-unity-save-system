# Threading, Async, and WebGL

`SaveSystem` runs heavy I/O and serialization safely without touching Unity objects off the main thread.

## Rules

- `LoadAsync` reads, decrypts, and parses JSON on a background thread (`Awaitable.BackgroundThreadAsync`) and returns to the main thread (`Awaitable.MainThreadAsync`) before instantiating prefabs or invoking `ISaveable` callbacks.
- `LoadAsync` rejects concurrent re-entry with `InvalidOperationException` while `IsBusy` is `true`.
- When `UseBackgroundWrite` is enabled, `Save()` captures the `SaveDocument` and serializes JSON on the **main thread**, then queues encryption and atomic file I/O onto a single-writer background worker (`latest wins`).
- Call `Flush()` before reading files externally; `Load()`, `Delete()`, `OnApplicationPause(true)`, `OnApplicationFocus(false)`, `OnApplicationQuit()`, and `OnDestroy()` call `Flush()` automatically.
- On WebGL (`UNITY_WEBGL && !UNITY_EDITOR`), `UseBackgroundWrite` is ignored (writes run inline on the main thread) and `FlexibleSave_SyncFileSystem` (`FS.syncfs`) is invoked after writes and deletes to persist IndexedDB.

## Example

```csharp
using System;
using System.Threading;
using FlexibleSave.Enhanced;
using UnityEngine;

public class AsyncSaveController : MonoBehaviour
{
    public async Awaitable LoadWithProgress(IProgress<float> progress, CancellationToken ct)
    {
        LoadResult result = await SaveSystem.Instance.LoadAsync(progress, ct);
        Debug.Log($"Load finished: {result.Status} in {result.Ms:F1} ms");
    }

    public async Awaitable SaveAndWait(CancellationToken ct)
    {
        SaveSystem.Instance.UseBackgroundWrite = true;
        SaveResult result = await SaveSystem.Instance.SaveAsync(ct);
        Debug.Log($"Save status: {result.Status}");
    }
}
```
