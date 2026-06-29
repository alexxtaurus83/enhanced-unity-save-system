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