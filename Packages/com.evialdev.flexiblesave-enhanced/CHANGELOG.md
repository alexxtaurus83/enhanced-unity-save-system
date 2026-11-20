# Changelog

## [2.0.1] - 2026-11-20

### Fixed
- **Entity hierarchy & lifecycle:**
  - `SaveableEntityRegistry.CaptureState` now removes stale root entries from `document.entities` when a previously saved root entity is reparented under another `SaveableEntity`.
  - `SaveableEntity.ApplyLoadedRecord` now detaches entities saved at root if they were reparented under another `SaveableEntity` before loading (while preserving plain non-`SaveableEntity` scene folder parents).
  - `SaveableEntityRegistry.ReinstantiateFromMeta` now detaches non-prefab child `SaveableEntity` subtrees before destroying the old parent prefab so scene children survive and re-attach on load.
  - `SaveableEntityRegistry.RegisterUnregisteredSceneEntities` is now invoked on `LoadStatus.NoSaveFile` and `SceneManager.sceneLoaded` so never-activated inactive scene entities are included in subsequent saves.
  - `SaveSystem.OnDestroy` no longer clears `SaveableEntityRegistry`, preserving `DontDestroyOnLoad` and multi-scene entities across scene `SaveSystem` replacements.
  - `SaveableEntity.RecordInitialChildIds` and `RestoreEntityRecord` now track and apply `deletedChildren` for nested `SaveableEntity` children inside prefabs.
- **Async, threading & error handling:**
  - Removed the shared `Stopwatch` between main-thread `Save()` and `BackgroundWriteLoop`; background writes now create a thread-local `Stopwatch`.
  - Added post-`MainThreadAsync()` destruction guards (`this == null`) in `LoadAsyncInternal` and `SaveAsync`.
  - Guarded `SaveSystem.Flush` against `m_disposed` / `ObjectDisposedException` races after `OnDestroy`.
  - `SaveSystem.ParsePayload` now rejects plaintext JSON payloads when an `ISaveEncryptor` is configured instead of silently bypassing encryption/HMAC verification.
  - `SaveableEntity.CaptureSingleComponent` now warns on duplicate `SaveKey` collisions and preserves prior saved state when `SaveState()` throws.
  - Deduplicated `.corrupt.<utc>.json` quarantine copies on repeated loads of the same corrupt primary file, eliminated duplicate `Debug.LogError` calls on sync write failures, and ensured coalesced `Save()` + `SaveAsync()` background batches invoke `Saved` once.
- **Editor & Samples:**
  - `PrefabAssetPostProcessor` now clears non-manual `m_ID` on all child `SaveableEntity` components, generates empty root `m_prefabID`s on import, prevents double-syncs on self-saves, and updates `SaveablePrefabs` incrementally.
  - `SaveableEntityEditor` `"New"` ID button now calls `entity.GenerateID()` in Play Mode so `SaveableEntityRegistry` updates live.
  - Added unique `m_childID` overrides to nested `Block1`–`Block4` instances in `Samples~/Basic/Prefabs/StackedBlock.prefab`, removed obsolete `m_autoSetPrefabID` YAML overrides across sample prefabs and `Scene2.unity`, and added `SaveablePrefabs` onboarding prompts in `SaveSystemEditor`, `SaveSystemManagerWindow`, and `StartupUtilities`.

### Changed
- **Performance:**
  - `StartupUtilities.CheckForScriptableObjectConflicts` now uses `TypeCache.GetTypesDerivedFrom<ISaveID>()` and `AssetDatabase.FindAssets` instead of `Resources.LoadAll<ScriptableObject>("")` on every Play Mode entry.
  - `StartupUtilities.CheckForPrefabConflicts` no longer runs full-project `SyncAllPrefabs()` on every Play Mode entry; `SaveablePrefabsEditor.SyncAllPrefabs` now scans `Assets` with `TryGetComponent<SaveableEntity>` instead of `Packages` and `GetComponentInChildren`.
  - `IDFinder.GetScriptableObject<T>` and `IDFinder.GetGameObject<T>` now cache lookups per type with automatic scene/load invalidation.
  - `SaveIDManagerEditor<T>` now caches ID conflict checks until hierarchy, project, undo, or inspector ID edits occur.
  - `SaveableEntity.ComputeRelativeParentPath` now returns `null` instead of `string.Empty` for direct children so `parentPath` is omitted from JSON; `SaveableEntityRegistry` and `SaveableEntity` reuse scratch buffers to reduce save/load allocations.

## [2.0.0] - 2026-11-20

> **Breaking rewrite:** 2.0.0 is a ground-up rewrite and is **not** save-file or API-compatible with 1.x (`SaveLoadSystem`).

### Added
- Standard UPM package layout (`com.evialdev.flexiblesave-enhanced`) for Unity 6.6+ (`6000.6`) with Runtime, Editor, and EditMode/PlayMode Test assemblies.
- Typed `SaveDocument`, `EntityRecord`, and `EntityMeta` format with `format: "FlexibleSave"`, `formatVersion: 1`, `savedAtUtc`, `entities`, and `deleted`.
- `SaveSystem.Migrations` delegate for upgrading older `formatVersion` documents.
- `SaveSystem` atomic write pipeline (`save.json.tmp` -> `File.Replace` -> `save.json` + `save.json.bak`), corrupt-file quarantine (`save.corrupt.<utc>.json`), backup fallback (`RestoredFromBackup`), and save guard (`!HasLoaded` or `IsCorrupt` blocks saving).
- Unity 6 `Awaitable` `LoadAsync(IProgress<float>, CancellationToken)` with background-thread read/decrypt/parse, monotonic progress, and `IsBusy` re-entry protection.
- Opt-in single-writer background save queue (`UseBackgroundWrite`) with `Flush()`, `OnApplicationPause(true)` / `OnApplicationQuit()` flush, and WebGL `FS.syncfs` support.
- Pluggable `FlexibleSave.Enhanced.Security.ISaveEncryptor` with `"FSV"` binary header and fast `LightSaveEncryptor` (cached SHA-256 keys, AES-128-CBC + 16-byte HMAC-SHA256).
- `ISaveable` C# default interface members, `SaveKey`, and `SaveState.As<T>(object)`.
- `SaveableEntity` manual ID mode, Editor duplicate validation, relative `parentPath` parenting, and Editor-only `m_childID` assignment via `AssetPostprocessor`.
- Native Unity 6.6 `[AutoStaticsCleanup]` support for Domain Reload Disabled (Fast Play Mode).
- `Runtime/link.xml` and `[Preserve]` attributes for IL2CPP stripping safety.

### Changed
- Renamed namespaces to `FlexibleSave.Enhanced`, `FlexibleSave.Enhanced.Security`, and `FlexibleSave.Enhanced.Editor`; renamed main component from `SaveLoadSystem` to `SaveSystem`.
- Removed `TypeNameHandling.Auto` and `StrictTypeSerializationBinder`; serialization now uses `TypeNameHandling.None` with a shared static `SaveJson` serializer.
- Scene entities absent from the save file are left untouched on load; only IDs recorded in `deleted` are destroyed.
- `SaveableEntityRegistry` tracks entities from `Awake` to `OnDestroy` (including inactive entities) in a single unified set.
- `SaveablePrefabs` asset moved to `Assets/Resources/FlexibleSave/SaveablePrefabs.asset` with lazy lookup caching.
- Rewrote Editor inspectors, `SaveSystemManagerWindow`, and `StartupUtilities` without private reflection or per-frame scene scans.

### Historical 1.x Pre-Rewrite Notes (from `what is new.md`)
- Replaced `BinaryFormatter` with `Newtonsoft.Json` and `PrivateFieldContractResolver`.
- Introduced GUID-based entity/prefab IDs, `Type.FullName` component keys, `FindObjectsByType`, `OnEnable`/`OnDisable` tracking, and the 3-stage `ISaveable` lifecycle.
