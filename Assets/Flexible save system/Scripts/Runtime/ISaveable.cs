using UnityEngine;

namespace SaveLoadSystem {
    /// <summary>
    /// Defines the contract for any component that wishes to persist its state.
    /// This interface manages the full lifecycle of saving and loading, including a robust
    /// three-stage loading process to handle complex dependencies between objects.
    /// <list type="bullet">
    /// <item><description><see cref="NeedsToBeSaved"/>: Determines if the object should be saved.</description></item>
    /// <item><description><see cref="NeedsReinstantiation"/>: Determines if the object should be recreated from a prefab on load.</description></item>
    /// <item><description><see cref="SaveState"/>: Captures the component's data.</description></item>
    /// <item><description><see cref="LoadState"/>: Restores raw data.</description></item>
    /// <item><description><see cref="AllObjectsStateLoaded"/>: Caches references to other components.</description></item>
    /// <item><description><see cref="AllObjectsDependenciesRestored"/>: Performs final setup logic.</description></item>
    /// <item><description><see cref="GotAddedAsChild"/>: A callback for when a saved object is parented to this object.</description></item>
    /// </list>
    /// </summary>
    public interface ISaveable
    {
        /// <summary>
        /// When returned false, the object will be ignored in the save progress.
        /// </summary>
        bool NeedsToBeSaved();

        /// <summary>
        /// Return true if this object needs to be reinstantiated at load, or false if simply loading state is enough.
        /// This only works for Prefab Objects.
        /// </summary>
        bool NeedsReinstantiation();

        /// <summary>
        /// Return the serializable data structure which should be stored.
        /// </summary>
        object SaveState();

        /// <summary>
        /// Load the same serializable data structure back into the object.
        /// At this stage, other objects may not exist yet. Only load raw data here.
        /// </summary>
        void LoadState(object state);

        /// <summary>
        /// Called after `LoadState` has been called on ALL objects.
        /// At this stage, all objects exist and have their raw data loaded.
        /// Use this to safely find other objects and cache references to their components.
        /// </summary>
        void AllObjectsStateLoaded(object state);

        /// <summary>
        /// Called last, after `AllObjectsStateLoaded` has been called on ALL objects.
        /// At this stage, all objects and their component references are guaranteed to be restored.
        /// Use this for final setup logic that depends on other objects being fully initialized (e.g., subscribing to events).
        /// </summary>
        void AllObjectsDependenciesRestored();

        /// <summary>
        /// Will be called when a loaded Object has been added to its saved parent
        /// which is a child of this gameObject.
        /// So you get notified here if any objects get added to any child gameObjects of this.
        /// </summary>
        void GotAddedAsChild(GameObject obj, GameObject hisParent);

    }
};
