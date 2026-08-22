using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EDAccountSwitcher.Core;
using Xunit;

namespace EDSwitcher.Tests
{
    public sealed class SettingsStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public SettingsStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "edswitcher-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "settings.json");

            // Keeps the tests away from %LOCALAPPDATA% and skips the legacy-file migration.
            SettingsStore.UseFilePath(_file);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Missing_file_returns_fallbacks()
        {
            Assert.Equal("Default", SettingsStore.GetString("AppTheme", "Default"));
            Assert.True(SettingsStore.GetBool("UiSounds", true));
            Assert.Equal(2.0, SettingsStore.GetDouble("AutoExitDelaySeconds", 2.0));
            Assert.Null(SettingsStore.GetString("NoSuchKey"));
        }

        [Fact]
        public void Round_trip_preserves_types()
        {
            Assert.True(SettingsStore.Set("AppTheme", "Dark"));
            Assert.True(SettingsStore.Set("UiSounds", false));
            Assert.True(SettingsStore.Set("AutoExitDelaySeconds", 7.5));
            Assert.True(SettingsStore.Set("AppLanguage", "be"));

            Assert.Equal("Dark", SettingsStore.GetString("AppTheme"));
            Assert.False(SettingsStore.GetBool("UiSounds", true));
            Assert.Equal(7.5, SettingsStore.GetDouble("AutoExitDelaySeconds", 2.0));
            Assert.Equal("be", SettingsStore.GetString("AppLanguage"));
        }

        [Fact]
        public void Values_survive_a_process_restart()
        {
            SettingsStore.Set("PreferredVersion", "edo");

            // Simulate a restart: same file, fresh look at it.
            SettingsStore.UseFilePath(_file);

            Assert.Equal("edo", SettingsStore.GetString("PreferredVersion"));
        }

        [Fact]
        public void Write_preserves_keys_it_does_not_know_about()
        {
            File.WriteAllText(_file, """
            {
              "PreferredVersion": "edo",
              "SomeFutureSetting": { "nested": [1, 2, 3] }
            }
            """);

            Assert.True(SettingsStore.Set("AppLanguage", "ru"));

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_file));
            Assert.Equal("edo", doc.RootElement.GetProperty("PreferredVersion").GetString());
            Assert.Equal("ru", doc.RootElement.GetProperty("AppLanguage").GetString());
            Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("SomeFutureSetting").ValueKind);
        }

        [Fact]
        public void Write_does_not_clobber_a_key_written_by_someone_else()
        {
            // Regression: SettingsPage used to hold an in-memory snapshot and overwrite
            // whatever OverviewPage had written in the meantime.
            SettingsStore.Set("AppTheme", "Dark");

            var raw = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_file));
            raw["PreferredVersion"] = "edh4";
            File.WriteAllText(_file, JsonSerializer.Serialize(raw));

            SettingsStore.Set("UiSounds", false);

            Assert.Equal("edh4", SettingsStore.GetString("PreferredVersion"));
            Assert.Equal("Dark", SettingsStore.GetString("AppTheme"));
            Assert.False(SettingsStore.GetBool("UiSounds", true));
        }

        [Fact]
        public void SetMany_writes_every_key_and_reports_them()
        {
            IReadOnlyCollection<string> reported = null;
            Action<IReadOnlyCollection<string>> handler = keys => reported = keys;

            SettingsStore.Changed += handler;
            try
            {
                Assert.True(SettingsStore.SetMany(new Dictionary<string, object>
                {
                    ["EdInstallPath"] = @"C:\Games\Elite",
                    ["LauncherPathBox"] = @"C:\Games\Elite\MinEdLauncher.exe"
                }));
            }
            finally
            {
                SettingsStore.Changed -= handler;
            }

            Assert.Equal(@"C:\Games\Elite", SettingsStore.GetString("EdInstallPath"));
            Assert.Equal(@"C:\Games\Elite\MinEdLauncher.exe", SettingsStore.GetString("LauncherPathBox"));
            Assert.NotNull(reported);
            Assert.Contains("EdInstallPath", reported);
            Assert.Contains("LauncherPathBox", reported);
        }

        [Fact]
        public void SetMany_with_nothing_to_write_is_a_no_op()
        {
            bool raised = false;
            Action<IReadOnlyCollection<string>> handler = _ => raised = true;

            SettingsStore.Changed += handler;
            try
            {
                Assert.True(SettingsStore.SetMany(new Dictionary<string, object>()));
                Assert.True(SettingsStore.SetMany(null));
            }
            finally
            {
                SettingsStore.Changed -= handler;
            }

            Assert.False(raised);
            Assert.False(File.Exists(_file));
        }

        [Fact]
        public void Corrupt_file_is_treated_as_empty_and_can_be_rewritten()
        {
            File.WriteAllText(_file, "{ this is not json");

            Assert.Equal("Default", SettingsStore.GetString("AppTheme", "Default"));
            Assert.True(SettingsStore.Set("AppTheme", "Light"));
            Assert.Equal("Light", SettingsStore.GetString("AppTheme"));
        }

        [Fact]
        public void Wrongly_typed_value_falls_back_instead_of_throwing()
        {
            File.WriteAllText(_file, """{ "UiSounds": "not a bool", "AutoExitDelaySeconds": "abc" }""");

            Assert.True(SettingsStore.GetBool("UiSounds", true));
            Assert.Equal(2.0, SettingsStore.GetDouble("AutoExitDelaySeconds", 2.0));
        }

        [Fact]
        public void String_value_is_parsed_for_bool_and_double()
        {
            File.WriteAllText(_file, """{ "UiSounds": "false", "AutoExitDelaySeconds": "12.5" }""");

            Assert.False(SettingsStore.GetBool("UiSounds", true));
            Assert.Equal(12.5, SettingsStore.GetDouble("AutoExitDelaySeconds", 2.0));
        }

        [Fact]
        public void Write_leaves_no_temp_file_behind()
        {
            SettingsStore.Set("AppTheme", "Dark");
            SettingsStore.Set("AppTheme", "Light");   // second write goes through the replace path

            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Failed_write_returns_false_and_records_the_reason()
        {
            // The "file" is an existing directory, so the final move cannot succeed.
            SettingsStore.UseFilePath(_dir);

            bool raised = false;
            Action<IReadOnlyCollection<string>> handler = _ => raised = true;

            SettingsStore.Changed += handler;
            try
            {
                Assert.False(SettingsStore.Set("AppTheme", "Dark"));
            }
            finally
            {
                SettingsStore.Changed -= handler;
            }

            Assert.NotNull(SettingsStore.LastError);
            Assert.False(raised);
        }
    }
}