# Error Handling & Recovery

`SaveSystem` catches read, decryption, parse, and write failures so corrupt saves never crash startup or overwrite valid progress.

## Rules

- Check `LoadResult.Status` (`Ok`, `NoSaveFile`, `RestoredFromBackup`, `Corrupt`) after `Load()` or `LoadAsync()`.
- When the primary save file fails decryption, parsing, or header validation, `SaveSystem` copies it aside to `<name>.corrupt.<utc>.<ext>` and attempts to restore `<file>.bak`.
- If `.bak` succeeds, `Load()` returns `LoadStatus.RestoredFromBackup` and keeps `IsCorrupt == false`.
- If no valid backup exists, `Load()` sets `IsCorrupt = true`, leaves the scene untouched, raises `Failed`, and blocks `Save()` (`SaveStatus.Blocked`) until `Delete()` or a manual fix resolves the state.
- Atomic writes write to `<file>.tmp` first and swap via `File.Replace` (or `File.Move` on first save), keeping the previous save intact if writing fails mid-operation.

## Example

```csharp
using FlexibleSave.Enhanced;
using UnityEngine;

public class SaveErrorHandler : MonoBehaviour
{
    public void LoadWithRecoveryPrompt()
    {
        LoadResult result = SaveSystem.Instance.Load();
        switch (result.Status)
        {
            case LoadStatus.RestoredFromBackup:
                Debug.LogWarning($"Recovered save from backup after error: {result.Error}");
                break;
            case LoadStatus.Corrupt:
                Debug.LogError($"Save file is corrupt and saving is blocked: {result.Error}");
                // Present UI choice to user before calling SaveSystem.Instance.Delete();
                break;
        }
    }
}
```
