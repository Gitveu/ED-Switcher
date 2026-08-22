using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EDAccountSwitcher.Core
{
    public static class SettingsStore
    {
        private const string FileName = "settings.json";

        private static readonly object Gate = new object();
        private static string _filePath;

        ///  Reason the last write failed, or null if it succeeded. 
        public static Exception LastError { get; private set; }

        ///  Raised after a successful write, with the keys that were written. 
        public static event Action<IReadOnlyCollection<string>> Changed;

        public static string FilePath
        {
            get
            {
                lock (Gate)
                {
                    if (_filePath == null)
                    {
                        _filePath = ResolveDefaultPath();
                        MigrateLegacyFile(_filePath);
                    }
                    return _filePath;
                }
            }
        }

        ///  Points the store at another file (portable mode, and tests). 
        public static void UseFilePath(string path)
        {
            lock (Gate) { _filePath = path; }
        }

        // ---------- reading ----------

        ///  Raw value: string, bool or double. Unknown JSON shapes are returned as JsonElement. 
        public static object Get(string key, object fallback = null)
        {
            lock (Gate)
            {
                var all = ReadAll();
                return all.TryGetValue(key, out object value) && value != null ? value : fallback;
            }
        }

        public static string GetString(string key, string fallback = null)
        {
            object value = Get(key);
            return value == null ? fallback : value as string ?? value.ToString();
        }

        public static bool GetBool(string key, bool fallback)
        {
            object value = Get(key);
            return value switch
            {
                bool b => b,
                string s when bool.TryParse(s, out bool parsed) => parsed,
                double d => Math.Abs(d) > double.Epsilon,
                _ => fallback
            };
        }

        public static double GetDouble(string key, double fallback)
        {
            object value = Get(key);
            return value switch
            {
                double d => d,
                int i => i,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => fallback
            };
        }

        // ---------- writing ----------

        ///  Writes one key. Returns false and fills <see cref="LastError"/> if the file could not be written. 
        public static bool Set(string key, object value) =>
            SetMany(new Dictionary<string, object> { [key] = value });

        ///  Writes several keys in a single file operation. 
        public static bool SetMany(IReadOnlyDictionary<string, object> values)
        {
            if (values == null || values.Count == 0) return true;

            bool ok;

            lock (Gate)
            {
                try
                {
                    // Read-before-write: keys written by another page since we last read are preserved.
                    var all = ReadAll();
                    foreach (var pair in values) all[pair.Key] = pair.Value;

                    string json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
                    WriteAtomic(FilePath, json);

                    LastError = null;
                    ok = true;
                }
                catch (Exception ex)
                {
                    LastError = ex;
                    ok = false;
                }
            }

            // Raised outside the lock: a handler may read or write settings itself.
            if (ok) Changed?.Invoke(values.Keys.ToArray());

            return ok;
        }

        // ---------- internals ----------

        private static Dictionary<string, object> ReadAll()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);

            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return result;

                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
                if (parsed == null) return result;

                foreach (var pair in parsed) result[pair.Key] = Unwrap(pair.Value);
            }
            catch (Exception ex)
            {
                // A corrupt file must not crash the app; it is treated as empty and rewritten on the next save.
                LastError = ex;
            }

            return result;
        }

        private static object Unwrap(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null => null,
            _ => element      // arrays/objects are kept verbatim so unknown settings survive a rewrite
        };

        private static void WriteAtomic(string path, string json)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);

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
            catch (IOException) { }                  // not supported on every file system
            catch (PlatformNotSupportedException) { }

            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        private static string ResolveDefaultPath()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ED Switcher");

                Directory.CreateDirectory(dir);
                return Path.Combine(dir, FileName);
            }
            catch (Exception ex)
            {
                LastError = ex;
                return Path.Combine(AppContext.BaseDirectory, FileName);
            }
        }

        ///  Copies a pre-existing settings.json from the exe folder once, then renames the original. 
        private static void MigrateLegacyFile(string target)
        {
            try
            {
                if (File.Exists(target)) return;

                string legacy = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(legacy)) return;
                if (string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase)) return;

                File.Copy(legacy, target);

                // Best-effort: the app folder may be read-only (Program Files).
                try { File.Move(legacy, legacy + ".migrated", true); } catch { }
            }
            catch (Exception ex)
            {
                LastError = ex;
            }
        }
    }
}