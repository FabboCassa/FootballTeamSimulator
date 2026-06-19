using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Fts.Services.Localization
{
    /// <summary>
    /// Loads flat key->text tables from Resources/Localization/{code}.json.
    /// Default language: saved preference, else system language, else English.
    /// Missing keys fall back to the English table, then to the key itself so
    /// untranslated text is immediately visible in play tests.
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        private const string PrefsKey = "fts.language";
        private const string FallbackLanguage = "en";

        private static readonly string[] Languages = { "en", "it" };

        private readonly Dictionary<string, string> _fallback;
        private Dictionary<string, string> _table;

        public string CurrentLanguage { get; private set; }

        public IReadOnlyList<string> AvailableLanguages => Languages;

        public LocalizationService()
        {
            _fallback = LoadTable(FallbackLanguage);
            CurrentLanguage = ResolveInitialLanguage();
            _table = CurrentLanguage == FallbackLanguage ? _fallback : LoadTable(CurrentLanguage);
        }

        public string Tr(string key)
        {
            if (_table.TryGetValue(key, out string text))
                return text;
            if (_fallback.TryGetValue(key, out string fallback))
                return fallback;

            Debug.LogWarning($"[Loc] Missing key '{key}'.");
            return key;
        }

        public string Tr(string key, params object[] args) => string.Format(Tr(key), args);

        public void SetLanguage(string code)
        {
            if (code == CurrentLanguage || System.Array.IndexOf(Languages, code) < 0)
                return;

            CurrentLanguage = code;
            _table = code == FallbackLanguage ? _fallback : LoadTable(code);
            PlayerPrefs.SetString(PrefsKey, code);
            PlayerPrefs.Save();
        }

        private static string ResolveInitialLanguage()
        {
            string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (System.Array.IndexOf(Languages, saved) >= 0)
                return saved;

            return Application.systemLanguage == SystemLanguage.Italian ? "it" : FallbackLanguage;
        }

        private static Dictionary<string, string> LoadTable(string code)
        {
            var asset = Resources.Load<TextAsset>($"Localization/{code}");
            if (asset == null)
            {
                Debug.LogWarning($"[Loc] Table 'Localization/{code}.json' not found in Resources.");
                return new Dictionary<string, string>();
            }

            return JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text)
                   ?? new Dictionary<string, string>();
        }
    }
}
