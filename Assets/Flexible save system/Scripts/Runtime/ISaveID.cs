
namespace SaveLoadSystem
{
    public interface ISaveID
    {
        /// <summary>
        /// Gets the unique identifier of the object.
        /// </summary>
        /// <returns>The unique ID string.</returns>
        string GetID();
        /// <summary>
        /// Sets the unique identifier of the object.
        /// </summary>
        /// <param name="ID">The unique ID string to set.</param>
        void SetID(string ID);
    }
}

