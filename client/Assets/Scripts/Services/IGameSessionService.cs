using Fts.Services.Persistence;

namespace Fts.Services
{
    /// <summary>
    /// Controls the lifetime of the active-career (Game) DI scope.
    /// Implemented in the App layer, where the scope hierarchy lives.
    /// </summary>
    public interface IGameSessionService
    {
        bool IsCareerActive { get; }

        /// <summary>Saves the freshly created career and opens the Hub.</summary>
        void StartNewCareer(CareerState newCareer);

        /// <summary>Loads the saved career and opens the Hub on success.</summary>
        SaveLoadStatus ContinueCareer();

        /// <summary>Saves, pops back to the main menu and disposes the Game scope.</summary>
        void EndCareer();
    }
}
