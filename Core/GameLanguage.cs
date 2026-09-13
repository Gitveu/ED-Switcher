using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EDAccountSwitcher.Core
{
    public static class GameLanguage
    {
        private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = "Deutsch",
            ["en"] = "English",
            ["es"] = "Español",
            ["fr"] = "Français",
            ["pt-BR"] = "Português (Brasil)",
            ["ru"] = "Русский"
        };

        public static IReadOnlyList<(string Code, string DisplayName)> GetAvailableLanguages(string installDir)
        {
            var languages = new List<(string, string)> { (null, "System default") };

            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                return languages;

            var folders = Directory.EnumerateDirectories(installDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && LanguageNames.ContainsKey(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

            foreach (var code in folders)
                languages.Add((code, LanguageNames[code]));

            return languages;
        }
    }
}