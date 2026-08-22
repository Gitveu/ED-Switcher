using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using EDAccountSwitcher.Localization;
using Xunit;

namespace EDSwitcher.Tests
{
    public sealed class LocalizationTests : IDisposable
    {
        public LocalizationTests() => LocalizationManager.Apply(AppLanguage.English, notify: false);

        public void Dispose() => LocalizationManager.Apply(AppLanguage.English, notify: false);

        // ---------- language resolution ----------

        [Theory]
        [InlineData(AppLanguage.English, new[] { "ru-RU" }, "en")]
        [InlineData(AppLanguage.Russian, new[] { "en-US" }, "ru")]
        [InlineData(AppLanguage.Belarusian, new[] { "en-US" }, "be")]
        [InlineData(AppLanguage.System, new[] { "ru-RU", "en-US" }, "ru")]
        [InlineData(AppLanguage.System, new[] { "be-BY", "ru-RU" }, "be")]
        [InlineData(AppLanguage.System, new[] { "de-DE", "ru-RU" }, "ru")]
        [InlineData(AppLanguage.System, new[] { "de-DE", "fr-FR" }, "en")]
        [InlineData(AppLanguage.System, new[] { "RU" }, "ru")]
        [InlineData(AppLanguage.System, new string[0], "en")]
        public void ResolveTag_picks_the_expected_language(AppLanguage selected, string[] systemTags, string expected)
        {
            Assert.Equal(expected, LocalizationManager.ResolveTag(selected, systemTags));
        }

        [Fact]
        public void ResolveTag_survives_null_and_garbage_input()
        {
            Assert.Equal("en", LocalizationManager.ResolveTag(AppLanguage.System, null));
            Assert.Equal("en", LocalizationManager.ResolveTag(AppLanguage.System, new string[] { null, "", "-" }));
        }

        // ---------- settings value <-> enum ----------

        [Theory]
        [InlineData("en", AppLanguage.English)]
        [InlineData("ru", AppLanguage.Russian)]
        [InlineData("be", AppLanguage.Belarusian)]
        [InlineData("RU", AppLanguage.Russian)]
        [InlineData("be-BY", AppLanguage.Belarusian)]
        [InlineData("System", AppLanguage.System)]
        [InlineData("nonsense", AppLanguage.System)]
        [InlineData("", AppLanguage.System)]
        [InlineData(null, AppLanguage.System)]
        public void Parse_maps_stored_values(string stored, AppLanguage expected)
        {
            Assert.Equal(expected, LocalizationManager.Parse(stored));
        }

        [Theory]
        [InlineData(AppLanguage.English, "en")]
        [InlineData(AppLanguage.Russian, "ru")]
        [InlineData(AppLanguage.Belarusian, "be")]
        [InlineData(AppLanguage.System, "System")]
        public void ToSettingValue_round_trips_through_Parse(AppLanguage language, string expected)
        {
            string stored = LocalizationManager.ToSettingValue(language);

            Assert.Equal(expected, stored);
            Assert.Equal(language, LocalizationManager.Parse(stored));
        }

        // ---------- lookup ----------

        [Fact]
        public void Apply_loads_the_selected_language()
        {
            var ru = LoadEmbeddedStrings("ru");
            LocalizationManager.Apply(AppLanguage.Russian, notify: false);

            Assert.Equal("ru", LocalizationManager.ActiveLanguageTag);
            Assert.Equal(ru["Nav_Settings"], LocalizationManager.Get("Nav_Settings"));

            var be = LoadEmbeddedStrings("be");
            LocalizationManager.Apply(AppLanguage.Belarusian, notify: false);

            Assert.Equal("be", LocalizationManager.ActiveLanguageTag);
            Assert.Equal(be["Nav_Settings"], LocalizationManager.Get("Nav_Settings"));
        }

        [Fact]
        public void Apply_raises_LanguageChanged_only_when_asked()
        {
            int raised = 0;
            EventHandler handler = (s, e) => raised++;

            LocalizationManager.LanguageChanged += handler;
            try
            {
                LocalizationManager.Apply(AppLanguage.Russian, notify: false);
                Assert.Equal(0, raised);

                LocalizationManager.Apply(AppLanguage.Belarusian);
                Assert.Equal(1, raised);
            }
            finally
            {
                LocalizationManager.LanguageChanged -= handler;
            }
        }

        [Fact]
        public void Unknown_key_is_visible_instead_of_silently_empty()
        {
            Assert.Equal("[NoSuchKey]", LocalizationManager.Get("NoSuchKey"));
            Assert.Equal(string.Empty, LocalizationManager.Get(null));
            Assert.Equal(string.Empty, LocalizationManager.Get(""));
        }

        [Fact]
        public void Format_substitutes_arguments()
        {
            LocalizationManager.Apply(AppLanguage.English, notify: false);

            string text = LocalizationManager.Format("Overview_DeleteConfirm", "Cmdr Jameson");

            Assert.Contains("Cmdr Jameson", text);
            Assert.DoesNotContain("{0}", text);
        }

        [Fact]
        public void Format_of_a_key_without_placeholders_does_not_throw()
        {
            Assert.Equal(LocalizationManager.Get("Nav_Settings"),
                         LocalizationManager.Format("Nav_Settings", "unused"));
        }

        // ---------- translation files ----------

        [Fact]
        public void Translations_have_exactly_the_same_keys()
        {
            var en = LoadEmbeddedStrings("en");
            var ru = LoadEmbeddedStrings("ru");
            var be = LoadEmbeddedStrings("be");

            Assert.NotEmpty(en);
            Assert.Empty(en.Keys.Except(ru.Keys));   // missing in ru.json
            Assert.Empty(ru.Keys.Except(en.Keys));   // extra in ru.json
            Assert.Empty(en.Keys.Except(be.Keys));   // missing in be.json
            Assert.Empty(be.Keys.Except(en.Keys));   // extra in be.json
        }

        [Fact]
        public void No_translation_value_is_empty()
        {
            foreach (string tag in new[] { "en", "ru", "be" })
                foreach (var pair in LoadEmbeddedStrings(tag))
                    Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{tag}.json: {pair.Key} is empty");
        }

        [Fact]
        public void Placeholders_match_across_translations()
        {
            var en = LoadEmbeddedStrings("en");

            foreach (string tag in new[] { "ru", "be" })
            {
                var other = LoadEmbeddedStrings(tag);

                foreach (var pair in en)
                {
                    if (!other.TryGetValue(pair.Key, out string translated)) continue;

                    Assert.Equal(Placeholders(pair.Value), Placeholders(translated));
                }
            }
        }

        [Fact]
        public void Every_key_used_in_xaml_exists_in_en_json()
        {
            string root = RepoRoot();
            if (!Directory.Exists(root)) return;   // sources not available next to the test binaries

            var known = LoadEmbeddedStrings("en").Keys;
            var missing = new List<string>();

            foreach (string xaml in Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(xaml)) continue;

                foreach (Match match in Regex.Matches(File.ReadAllText(xaml), @"loc:Loc\s+Key\s*=\s*(\w+)"))
                {
                    string key = match.Groups[1].Value;
                    if (!known.Contains(key)) missing.Add($"{Path.GetFileName(xaml)}: {key}");
                }
            }

            Assert.Empty(missing);
        }

        [Fact]
        public void Every_key_used_in_code_exists_in_en_json()
        {
            string root = RepoRoot();
            if (!Directory.Exists(root)) return;

            var known = LoadEmbeddedStrings("en").Keys;
            var missing = new List<string>();

            foreach (string source in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(source) || source.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")) continue;

                // L.Get("Key") / LocalizationManager.Format("Key", ...)
                foreach (Match match in Regex.Matches(File.ReadAllText(source), @"\.(?:Get|Format)\(""(\w+)"""))
                {
                    string key = match.Groups[1].Value;
                    if (!known.Contains(key)) missing.Add($"{Path.GetFileName(source)}: {key}");
                }
            }

            Assert.Empty(missing);
        }

        // ---------- helpers ----------

        private static SortedSet<string> Placeholders(string value) =>
            new SortedSet<string>(Regex.Matches(value ?? "", @"\{(\d+)").Select(m => m.Groups[1].Value));

        private static bool IsBuildArtifact(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

        private static Dictionary<string, string> LoadEmbeddedStrings(string tag)
        {
            Assembly assembly = typeof(LocalizationTests).Assembly;
            string name = assembly.GetManifestResourceNames()
                                  .Single(n => n.EndsWith("." + tag + ".json", StringComparison.OrdinalIgnoreCase));

            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }

        /// <summary>Repo root, derived from this file's compile-time path (tests\..).</summary>
        private static string RepoRoot([CallerFilePath] string thisFile = null) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), ".."));
    }
}