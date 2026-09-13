using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EDAccountSwitcher.Core
{
    public static class MinEdLauncherSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "min-ed-launcher",
            "settings.json");

        public static string? GetLanguage()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return null;

                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("language", out var lang))
                    return lang.ValueKind == JsonValueKind.Null ? null : lang.GetString();
            }
            catch { }

            return null;
        }

        public static void SetLanguage(string? language)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var settings = File.Exists(SettingsPath)
                    ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(SettingsPath))
                    : new JsonElement();

                using var doc = JsonDocument.Parse("{}");
                var options = new JsonSerializerOptions { WriteIndented = true };

                if (settings.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in settings.EnumerateObject())
                        dict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText()
                        };

                    if (language == null)
                        dict["language"] = null;
                    else
                        dict["language"] = language;

                    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dict, options));
                }
                else
                {
                    var minimal = new { language = language };
                    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(minimal, options));
                }
            }
            catch { }
        }
    }
}