using UnityEngine;

namespace FlexibleSave.Enhanced
{
    /// <summary>
    /// Defines the contract for components that persist state through <see cref="SaveSystem"/>.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>
        /// Stable component key in the save document. Return <c>null</c> to use <c>GetType().FullName</c>.
        /// </summary>
        string SaveKey => null;

        /// <summary>
        /// Returns false to skip this component during saving.
        /// </summary>
        bool NeedsToBeSaved() => true;

        /// <summary>
        /// Returns true if the owning prefab entity should be destroyed and reinstantiated on load.
        /// </summary>
        bool NeedsReinstantiation() => false;

        /// <summary>
        /// Captures the serializable state object for this component.
        /// </summary>
        object SaveState();

        /// <summary>
        /// Restores component state from the saved payload (typically a <see cref="Newtonsoft.Json.Linq.JToken"/>).
        /// </summary>
        void LoadState(object state);

        /// <summary>
        /// Called after all saved entities have executed <see cref="LoadState"/>.
        /// </summary>
        void AllObjectsStateLoaded(object state) { }

        /// <summary>
        /// Called after all saved entities have executed <see cref="AllObjectsStateLoaded"/>.
        /// </summary>
        void AllObjectsDependenciesRestored() { }

        /// <summary>
        /// Called when a loaded child <see cref="SaveableEntity"/> is re-parented under this object or its descendants.
        /// </summary>
        void GotAddedAsChild(GameObject obj, GameObject hisParent) { }
    }
}
