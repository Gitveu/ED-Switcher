using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EDAccountSwitcher.Localization
{
    public enum AppLanguage
    {
        ///  Follow the system language; falls back to English if it is not en/ru/be. 
        System,
        English,
        Russian,
        Belarusian
    }

    public static class LocalizationManager
    {
        public const string FallbackLanguageTag = "en";

        private static readonly string[] SupportedTags = { "en", "ru", "be" };

        private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _fallbackStrings = new(StringComparer.OrdinalIgnoreCase);

        ///  What the user picked in Settings (may be <see cref="AppLanguage.System"/>). 
        public static AppLanguage Selected { get; private set; } = AppLanguage.System;

        ///  The language actually in use: "en", "ru" or "be". 
        public static string ActiveLanguageTag { get; private set; } = FallbackLanguageTag;

        ///  Raised after the language changed. The UI reloads itself in response. 
        public static event EventHandler LanguageChanged;

        ///  Call once on startup, before any XAML page is loaded. 
        public static void Initialize(string settingValue) => Apply(Parse(settingValue), notify: false);

        public static AppLanguage Parse(string settingValue) =>
            (settingValue ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "en" or "en-us" or "english" => AppLanguage.English,
                "ru" or "ru-ru" or "russian" => AppLanguage.Russian,
                "be" or "be-by" or "belarusian" => AppLanguage.Belarusian,
                _ => AppLanguage.System
            };

        public static string ToSettingValue(AppLanguage language) => language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",
            AppLanguage.Belarusian => "be",
            _ => "System"
        };

        public static void Apply(AppLanguage language, bool notify = true)
        {
            Selected = language;
            ActiveLanguageTag = ResolveTag(language, GetUserPreferredLanguages());

            _fallbackStrings = LoadStrings(FallbackLanguageTag);
            _strings = ActiveLanguageTag == FallbackLanguageTag
                ? _fallbackStrings
                : LoadStrings(ActiveLanguageTag);

            ApplyCulture(ActiveLanguageTag);

            if (notify) LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string ResolveTag(AppLanguage selected, IEnumerable<string> preferredSystemTags)
        {
            if (selected == AppLanguage.English) return "en";
            if (selected == AppLanguage.Russian) return "ru";
            if (selected == AppLanguage.Belarusian) return "be";

            foreach (string tag in preferredSystemTags ?? Enumerable.Empty<string>())
            {
                string twoLetter = (tag ?? string.Empty).Split('-')[0].ToLowerInvariant();
                if (SupportedTags.Contains(twoLetter)) return twoLetter;
            }

            return FallbackLanguageTag;
        }
        public static string DetectSystemLanguage() => ResolveTag(AppLanguage.System, GetUserPreferredLanguages());

        private static IEnumerable<string> GetUserPreferredLanguages()
        {
            var tags = new List<string>();

            try { tags.AddRange(Windows.System.UserProfile.GlobalizationPreferences.Languages); }
            catch { }

            try { tags.Add(CultureInfo.CurrentUICulture.Name); } catch { }
            try { tags.Add(CultureInfo.InstalledUICulture.Name); } catch { }

            return tags.Where(t => !string.IsNullOrWhiteSpace(t));
        }

        private static void ApplyCulture(string tag)
        {
            try
            {
                var culture = new CultureInfo(tag switch
                {
                    "ru" => "ru-RU",
                    "be" => "be-BY",
                    _ => "en-US"
                });

                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch { }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_strings.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)) return value;
            if (_fallbackStrings.TryGetValue(key, out string fallback) && !string.IsNullOrEmpty(fallback)) return fallback;
            return "[" + key + "]";
        }

        public static string Format(string key, params object[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, Get(key), args); }
            catch { return Get(key); }
        }

        private static Dictionary<string, string> LoadStrings(string tag)
        {
            var result = LoadEmbedded(tag);

            // Optional override for community edits: <exe dir>\Localization\<tag>.json
            try
            {
                string external = Path.Combine(AppContext.BaseDirectory, "Localization", tag + ".json");
                if (File.Exists(external))
                {
                    foreach (var pair in Deserialize(File.ReadAllText(external)))
                        result[pair.Key] = pair.Value;
                }
            }
            catch { }

            return result;
        }

        private static Dictionary<string, string> LoadEmbedded(string tag)
        {
            try
            {
                Assembly assembly = typeof(LocalizationManager).Assembly;
                string suffix = "." + tag + ".json";
                string name = assembly.GetManifestResourceNames()
                                      .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                if (name == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return Deserialize(reader.ReadToEnd());
            }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        private static Dictionary<string, string> Deserialize(string json)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return parsed == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }
    }
}