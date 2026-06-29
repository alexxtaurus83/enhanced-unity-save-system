# Plan: Document Changes to Flexible Save System

## Goal
Review the manual changelog provided by the user against the current Git working tree, ensure all changes are documented, and prepare two files: `commit-message.md` and `what is new.md`.

## Analysis of Code vs. Provided Documentation
I have reviewed the `git status` and `git diff` of the working directory. The manual documentation you provided is incredibly thorough and covers almost all major architectural and feature changes! 

Here are a few additional technical details I discovered in the code that we should add to the final documentation:
1. **Assembly Definition:** Added an Assembly Definition (`FlexibleSaveSystemEditor.asmdef`) for the Editor scripts to improve compilation times and project structure.
2. **Newtonsoft.Json Private Fields:** Added `PrivateFieldContractResolver` to ensure `Newtonsoft.Json` correctly serializes private fields, replacing the deep integration `BinaryFormatter` had.
3. **SaveIDManagerEditor UI Improvements:** The `Prefab ID` field in the inspector is now read-only to prevent accidental modification, and a "Copy" button was added for convenience.
4. **StartupUtilities QoL:** When clicking "Try to resolve" on a conflict popup, the system now automatically searches for and selects the `SaveablePrefabs` asset in the Project window.
5. **SaveLoadSystemEditor UI:** The inspector has been completely reorganized with clear headers ("Save File Configuration", "Encryption Settings", "Debug Controls") and helpful info boxes.

## Proposed Output Files

### Task 1: Create `commit-message.md`
This file will contain a condensed version of the changes, suitable for a Git commit message.

```text
Major overhaul: Secure JSON Serialization, GUID IDs, and Async Loading

Security:
- Replaced BinaryFormatter with Newtonsoft.Json (mitigates deserialization risks).
- Added optional AES encryption for save files.

Reliability & Performance:
- Transitioned to GUIDs for Prefab and Child IDs, ensuring persistence across renames and hierarchy changes.
- Switched to using `Type.FullName` for component saving instead of execution order.
- Optimized core loops in `SaveableEntityManager` by removing `FindObjectsByType` in favor of a cached active entity registry.
- Fixed transform saving logic when `saveTransform` is false.
- Handled saving/loading of inactive GameObjects.

Architecture & API:
- Refactored `ISaveable` into a robust three-stage loading pipeline (`LoadState`, `AllObjectsStateLoaded`, `AllObjectsDependenciesRestored`).
- Added asynchronous loading (`LoadAsync`) with progress callbacks.

Editor & Tooling:
- Added new "Save System Manager" window with Live Dashboard and Save File Inspector.
- Added `FlexibleSaveSystemEditor.asmdef` for cleaner compilation.
- Improved Inspector layouts and conflict resolution popups.
- Replaced `goto` statements with standard control flow.
- Improved exception handling for legacy save files and unhandled component saves.
```

### Task 2: Create `what is new.md`
This file will contain the detailed, user-facing changelog, integrating your manual notes with the new technical discoveries.

```markdown
# What's New in Flexible Save System

### Security
- **[Security]** Replaced the insecure and obsolete `BinaryFormatter` with `Newtonsoft.Json` for all serialization. This mitigates critical deserialization vulnerabilities. Includes a custom `PrivateFieldContractResolver` to ensure private fields are seamlessly serialized.
- **[Feature]** Added optional AES encryption for save files. This can be enabled via a new checkbox in the `SaveLoadSystem` inspector.
- **[Feature]** Added a field to the `SaveLoadSystem` inspector to set a custom encryption key.

### Reliability & Bug Fixes
- **[Improvement]** The ID system has been completely overhauled to use GUIDs, making it significantly more robust and reliable.
- **[Improvement]** Prefab IDs (`m_prefabID`) are now persistent GUIDs stored with the asset. This prevents saves from breaking when a prefab file is renamed. The field is now read-only in the inspector with a convenient "Copy" button.
- **[Improvement]** Child object IDs (`m_childID`) within prefabs are now persistent GUIDs, preventing saves from breaking if the prefab hierarchy is changed.
- **[Improvement]** Keys for saving `ISaveable` component data now use the component's full type name (`Type.FullName`) instead of its index order. This prevents data corruption when reordering components on a GameObject.
- **[Fix]** Restored and improved the conflict detection mechanism in the `SaveablePrefabs` editor. It now correctly detects when duplicated prefab assets share the same `prefabID` and provides an easy way to generate a new ID directly from the inspector.
- **[Fix]** Corrected a logic error where transform data was still being written to the save file even when the `saveTransform` flag was set to `false`.
- **[Fix]** Fixed a compile error in `StartupUtilities.cs` referencing a deleted method. The startup check now correctly informs the user about conflicts and automatically locates and selects the `SaveablePrefabs` asset to help fix them.
- **[Fix]** The system now correctly handles saving and loading of GameObjects that are inactive (`disabled`) in the scene at startup.

### Performance
- **[Optimization]** Removed all uses of `FindObjectsByType` from the core save/load loops in `SaveableEntityManager`. The system now tracks active saveable objects via `OnEnable`/`OnDisable` into a cached registry, resulting in a massive performance increase, especially in large scenes.
- **[Optimization]** Added an Assembly Definition file (`FlexibleSaveSystemEditor.asmdef`) for the Editor scripts to improve project compilation times.

### Code Quality & Maintainability
- **[Refactor]** Removed all `goto` statements from `SaveableEntityManager` and `StartupUtilities`, replacing them with standard control flow to improve code readability.
- **[Documentation]** Created a detailed `FRAMEWORK_DESCRIPTION.md` file outlining the system's architecture, identified issues, and the full modification plan.
- **[Documentation]** Updated documentation to explain how the new ID system handles various scenarios like prefab instantiation and duplication.

### Editor Tools
- **[Feature]** Created a new "Save System Manager" editor window (`Tools/Flexible Save System/Manager Window`).
- **[Feature]** Added a "Live Dashboard" tab to the manager window to monitor all registered saveable objects in Play Mode and provide quick access to Save/Load/Delete functions.
- **[Feature]** Added a "Save File Inspector" tab to the manager window, allowing developers to load a `.save` file, decrypt it if necessary, and view its contents in a structured tree view for easy debugging.
- **[Improvement]** Completely overhauled the `SaveLoadSystem` inspector with categorized settings ("Save File Configuration", "Encryption Settings", "Debug Controls") and informative HelpBoxes.
- **[Improvement]** Added `[DisallowMultipleComponent]` attribute to `SaveableEntity` and `SaveLoadSystem` to prevent incorrect setup in the editor.
- **[Fix]** Added exception handling to `LoadFile` to gracefully manage `JsonSerializationException` errors that can occur when loading a save file created before a code refactoring (e.g., namespace change). The system now logs a descriptive error instead of crashing.
- **[Fix]** Added exception handling to the "Save System Manager" window to prevent `GUILayout` errors when an exception occurs during a Save/Load operation initiated from the editor.
- **[Fix]** Added a `try-catch` block within `SaveableEntity` to gracefully handle `NotImplementedException`. If a component's `SaveState` is not implemented, it now logs a detailed error pointing to the specific GameObject and script instead of throwing an unhandled exception.

### Architecture & API
- **[Major Feature]** Implemented an asynchronous loading pipeline (`LoadAsync`) with callbacks for progress updates, allowing developers to easily create loading bars.
- **[Major Improvement]** Reworked the `ISaveable` interface and loading lifecycle into a robust three-stage process (`LoadState`, `AllObjectsStateLoaded`, `AllObjectsDependenciesRestored`) to correctly handle complex object dependencies and ensure all data is loaded before references are restored.
```

## Next Steps
Once this plan is finalized, an implementation agent will use this plan to write out `commit-message.md` and `what is new.md` directly into the project directory.