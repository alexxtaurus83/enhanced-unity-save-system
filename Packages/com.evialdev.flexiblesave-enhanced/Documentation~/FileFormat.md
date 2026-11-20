# File Format and Migrations

Flexible Save Enhanced writes plain JSON (`TypeNameHandling.None`) with no `$type` assembly metadata.

## Rules

- Every valid save file contains `"format": "FlexibleSave"`, `"formatVersion": 1`, `"savedAtUtc"`, `"entities"`, and `"deleted"`.
- Files with a missing/mismatched `format` or an unhandled `formatVersion` are treated as `LoadStatus.Corrupt` and are never silently overwritten.
- Register `SaveSystem.Migrations` before calling `Load()` when upgrading older `formatVersion` saves. Your delegate must update `formatVersion` to `SaveDocument.CurrentFormatVersion` (`1`) and return the migrated `JObject`.
- Component entries are keyed by `ISaveable.SaveKey` when non-empty, otherwise `GetType().FullName`.
- Saving merges live scene entities into the last-loaded `SaveDocument` without re-reading disk, preserving entries for entities not present in the currently loaded scene and applying `deleted` tombstones.

## Example

```json
{
  "format": "FlexibleSave",
  "formatVersion": 1,
  "savedAtUtc": 1795196596909,
  "entities": {
    "player": {
      "meta": {
        "name": "Player",
        "id": "player",
        "hasPrefab": false,
        "saveTransform": false,
        "needsReinstantiation": false
      },
      "components": {
        "player.health": { "health": 100 }
      }
    }
  },
  "deleted": []
}
```

```csharp
SaveSystem.Migrations = (root, version) =>
{
    if (version == 0)
    {
        root["formatVersion"] = 1;
    }
    return root;
};
```
