namespace FlexibleSave.Enhanced
{
    /// <summary>
    /// Implemented by objects (such as <see cref="SaveableEntity"/> or <see cref="UnityEngine.ScriptableObject"/> assets)
    /// that expose a persistent identifier.
    /// </summary>
    public interface ISaveID
    {
        string GetID();
        void SetID(string id);
    }
}
