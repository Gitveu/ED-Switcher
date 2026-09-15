using System;
using System.IO;
using System.Text.Json;
using EDAccountSwitcher.Core;
using Xunit;

namespace EDSwitcher.Tests
{
    public sealed class MinEdLauncherSettingsTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public MinEdLauncherSettingsTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "mined-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "settings.json");

            MinEdLauncherSettings.UseFilePath(_file);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
            MinEdLauncherSettings.UseFilePath(null);
        }

        [Fact]
        public void Preserves_Arrays_And_Objects_When_Setting_Language()
        {
            string originalJson = """
            {
              "apiUri": "https://api.zaonce.net",
              "watchForCrashes": false,
              "language": "ru",
              "autoUpdate": true,
              "checkForLauncherUpdates": true,
              "maxConcurrentDownloads": 4,
              "forceUpdate": [],
              "processes": [],
              "shutdownProcesses": [],
              "filterOverrides": [
                { "sku": "FORC-FDEV-DO-1000", "filter": "edo" },
                { "sku": "FORC-FDEV-DO-38-IN-40", "filter": "edh4" }
              ],
              "additionalProducts": []
            }
            """.Replace("\r\n", "\n").Replace("\n", "\r\n");

            File.WriteAllText(_file, originalJson);

            // Change language to null (system default)
            MinEdLauncherSettings.SetLanguage(null);
            Assert.Null(MinEdLauncherSettings.GetLanguage());

            string resultText = File.ReadAllText(_file);

            // Verify that filterOverrides lines and formatting did not lose spaces or compact layout
            Assert.Contains("    { \"sku\": \"FORC-FDEV-DO-1000\", \"filter\": \"edo\" },", resultText);
            Assert.Contains("    { \"sku\": \"FORC-FDEV-DO-38-IN-40\", \"filter\": \"edh4\" }", resultText);
            Assert.EndsWith(Environment.NewLine, resultText);

            using var doc = JsonDocument.Parse(resultText);
            var root = doc.RootElement;

            Assert.Equal(JsonValueKind.Null, root.GetProperty("language").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("forceUpdate").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("filterOverrides").ValueKind);
            Assert.Equal(2, root.GetProperty("filterOverrides").GetArrayLength());
            Assert.Equal("FORC-FDEV-DO-1000", root.GetProperty("filterOverrides")[0].GetProperty("sku").GetString());
            Assert.Equal(JsonValueKind.False, root.GetProperty("watchForCrashes").ValueKind);
            Assert.Equal(4, root.GetProperty("maxConcurrentDownloads").GetInt32());

            // Change language to "en"
            MinEdLauncherSettings.SetLanguage("en");
            Assert.Equal("en", MinEdLauncherSettings.GetLanguage());

            using var doc2 = JsonDocument.Parse(File.ReadAllText(_file));
            Assert.Equal("en", doc2.RootElement.GetProperty("language").GetString());
            Assert.Equal(JsonValueKind.Array, doc2.RootElement.GetProperty("filterOverrides").ValueKind);
        }

        [Fact]
        public void Repairs_Corrupted_Stringified_Arrays_And_Objects()
        {
            string corruptedJson = """
            {
              "apiUri": "https://api.zaonce.net",
              "watchForCrashes": false,
              "language": null,
              "autoUpdate": true,
              "checkForLauncherUpdates": true,
              "maxConcurrentDownloads": 4,
              "forceUpdate": "[]",
              "processes": "[]",
              "shutdownProcesses": "[]",
              "filterOverrides": "[\n   { \u0022sku\u0022: \u0022FORC-FDEV-DO-1000\u0022, \u0022filter\u0022: \u0022edo\u0022 },\n   { \u0022sku\u0022: \u0022FORC-FDEV-DO-38-IN-40\u0022, \u0022filter\u0022: \u0022edh4\u0022 }\n ]",
              "additionalProducts": "[]"
            }
            """;

            File.WriteAllText(_file, corruptedJson);

            // Setting language triggers repair
            MinEdLauncherSettings.SetLanguage("ru");

            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            var root = doc.RootElement;

            Assert.Equal("ru", root.GetProperty("language").GetString());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("forceUpdate").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("processes").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("shutdownProcesses").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("filterOverrides").ValueKind);
            Assert.Equal(2, root.GetProperty("filterOverrides").GetArrayLength());
            Assert.Equal("FORC-FDEV-DO-1000", root.GetProperty("filterOverrides")[0].GetProperty("sku").GetString());
            Assert.Equal("edo", root.GetProperty("filterOverrides")[0].GetProperty("filter").GetString());
        }
    }
}
