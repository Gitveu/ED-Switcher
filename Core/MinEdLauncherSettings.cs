using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EDAccountSwitcher.Core
{
    public static class MinEdLauncherSettings
    {
        private static string? _settingsPath;

        public static string SettingsPath => _settingsPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "min-ed-launcher",
            "settings.json");

        public static void UseFilePath(string? path) => _settingsPath = path;

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

                JsonObject root;
                if (File.Exists(SettingsPath))
                {
                    var node = JsonNode.Parse(File.ReadAllText(SettingsPath));
                    root = node as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                // Repair any properties that were accidentally serialized as strings by previous buggy versions
                RepairCorruptedProperties(root);

                if (language == null)
                    root["language"] = null;
                else
                    root["language"] = language;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = root.ToJsonString(options);
                WriteAtomic(SettingsPath, json);
            }
            catch { }
        }

        private static void RepairCorruptedProperties(JsonObject root)
        {
            var keysToRepair = new List<(string Key, JsonNode Reparsed)>();

            foreach (var kvp in root)
            {
                if (kvp.Value is JsonValue jv && jv.TryGetValue<string>(out var str))
                {
                    string trimmed = str.Trim();
                    if ((trimmed.StartsWith("[") && trimmed.EndsWith("]")) ||
                        (trimmed.StartsWith("{") && trimmed.EndsWith("}")))
                    {
                        try
                        {
                            var reparsed = JsonNode.Parse(trimmed);
                            if (reparsed is JsonArray || reparsed is JsonObject)
                            {
                                keysToRepair.Add((kvp.Key, reparsed));
                            }
                        }
                        catch { }
                    }
                }
            }

            foreach (var (key, reparsed) in keysToRepair)
            {
                root[key] = reparsed;
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }

            try
            {
                File.Replace(tmp, path, null);
                return;
            }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }

            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }
}
