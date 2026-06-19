namespace Fts.Services.Persistence
{
    public enum SaveLoadStatus
    {
        Success,
        NotFound,
        Corrupted,
        IncompatibleVersion
    }

    /// <summary>
    /// Career persistence (ARCHITECTURE.md §5.2). Single-slot for now.
    /// Local gzip-JSON in single player; a cloud implementation arrives with the backend.
    /// </summary>
    public interface ISaveRepository
    {
        bool HasSave { get; }

        void Save(CareerState state);

        /// <summary>Never throws: corruption is reported via the status.</summary>
        SaveLoadStatus TryLoad(out CareerState state);

        void Delete();
    }
}
