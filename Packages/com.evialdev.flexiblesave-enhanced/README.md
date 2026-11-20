# Flexible Save Enhanced

A fast, modular JSON save system for **Unity 6.6+** (`6000.6`).

## Installation

Add via Git URL in Unity Package Manager:

```
https://github.com/alexxtaurus83/flexible-save-enhanced.git?path=Packages/com.evialdev.flexiblesave-enhanced
```

## 5-Minute Quick Start

1. Add `SaveSystem` (`FlexibleSave.Enhanced.SaveSystem`) to a GameObject in your startup scene.
2. Add `SaveableEntity` to GameObjects you want to persist.
3. Implement `ISaveable` (`SaveKey`, `SaveState()`, `LoadState(object state)` using `SaveState.As<T>(state)`).
4. Call `SaveSystem.Instance.Load()` (or `await SaveSystem.Instance.LoadAsync()`) before calling `SaveSystem.Instance.Save()`.

See `Documentation~/GettingStarted.md` for full guides.

## Credits

Based on [Flexible Save System](https://github.com/KROIA/Flexible-save-system) by KROIA, MIT licensed. 2.0 is a rewrite and is not save- or API-compatible with the original.

## License

MIT — see [LICENSE.md](LICENSE.md).
