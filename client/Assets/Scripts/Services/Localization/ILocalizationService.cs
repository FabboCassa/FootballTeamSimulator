using System.Collections.Generic;

namespace Fts.Services.Localization
{
    /// <summary>
    /// String tables (ARCHITECTURE.md §8: localization from day one).
    /// Tables are flat key->text JSON files in Resources/Localization/{code}.json —
    /// one file per language, trivially handed to a translator.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>ISO 639-1 code of the active language ("en", "it").</summary>
        string CurrentLanguage { get; }

        IReadOnlyList<string> AvailableLanguages { get; }

        /// <summary>Text for the key; falls back to English, then to the key itself (visible marker).</summary>
        string Tr(string key);

        /// <summary>Text for the key with string.Format arguments.</summary>
        string Tr(string key, params object[] args);

        /// <summary>Switches language and persists the choice.</summary>
        void SetLanguage(string code);
    }
}
