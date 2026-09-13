using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EDAccountSwitcher.Core
{
    public static class MinEdLauncherSettings
    {
        private static string? _settingsPath;

        private static readonly Regex LanguagePropertyRegex = new Regex(
            @"(""language""\s*:\s*)(?:null|""[^""]*"")",
            RegexOptions.Compiled);

        private static readonly Regex FilterOverrideItemRegex = new Regex(
            @"\{\s+""sku"":\s*""([^""]+)"",\s+""filter"":\s*""([^""]+)""\s+\}",
            RegexOptions.Compiled);

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

                if (File.Exists(SettingsPath))
                {
                    string content = File.ReadAllText(SettingsPath);

                    // If file was corrupted by previous buggy versions (contains escaped quotes or serialized string arrays), repair it
                    if (content.Contains("\\u0022") || content.Contains(": \"[]\"") || content.Contains(": \"[\n"))
                    {
                        var node = JsonNode.Parse(content);
                        var root = node as JsonObject ?? new JsonObject();
                        RepairCorruptedProperties(root);
                        root["language"] = language;
                        WriteAtomic(SettingsPath, FormatRepairedJson(root));
                        return;
                    }

                    // Otherwise, do an in-place update of the "language" property to preserve 100% of
                    // the original formatting, indentation, single-line objects, spaces, and empty trailing lines.
                    string langLiteral = language == null ? "null" : $"\"{language}\"";
                    string updated;

                    if (LanguagePropertyRegex.IsMatch(content))
                    {
                        updated = LanguagePropertyRegex.Replace(content, $"$1{langLiteral}");
                    }
                    else
                    {
                        int firstBrace = content.IndexOf('{');
                        if (firstBrace >= 0)
                        {
                            string nl = content.Contains("\r\n") ? "\r\n" : "\n";
                            string insert = $"{nl}  \"language\": {langLiteral},";
                            updated = content.Insert(firstBrace + 1, insert);
                        }
                        else
                        {
                            updated = $"{{\n  \"language\": {langLiteral}\n}}\n";
                        }
                    }

                    // Ensure final newline is never lost (empty line at EOF)
                    if (!updated.EndsWith("\n"))
                    {
                        updated += updated.Contains("\r\n") ? "\r\n" : "\n";
                    }

                    WriteAtomic(SettingsPath, updated);
                }
                else
                {
                    var root = new JsonObject
                    {
                        ["language"] = language
                    };
                    WriteAtomic(SettingsPath, FormatRepairedJson(root));
                }
            }
            catch { }
        }

        private static string FormatRepairedJson(JsonObject root)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = root.ToJsonString(options);

            // Collapse multi-line filter override items back to compact single-line objects:
            // { "sku": "...", "filter": "..." }
            json = FilterOverrideItemRegex.Replace(json, @"{ ""sku"": ""$1"", ""filter"": ""$2"" }");

            if (!json.EndsWith("\n"))
            {
                json += Environment.NewLine;
            }

            return json;
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

