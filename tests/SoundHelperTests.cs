using System;
using System.IO;
using EDAccountSwitcher.Core;
using Xunit;

namespace EDSwitcher.Tests
{
    public sealed class SoundHelperTests : IDisposable
    {
        private readonly string _dir;

        public SoundHelperTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "edswitcher-sound-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            SettingsStore.UseFilePath(Path.Combine(_dir, "settings.json"));
        }

        public void Dispose()
        {
            SettingsStore.Set("UiSounds", true);
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Sounds_are_on_when_nothing_was_configured()
        {
            Assert.True(SoundHelper.IsEnabled);
        }

        [Fact]
        public void Toggling_the_setting_takes_effect_without_a_restart()
        {
            Assert.True(SettingsStore.Set("UiSounds", false));
            Assert.False(SoundHelper.IsEnabled);

            Assert.True(SettingsStore.Set("UiSounds", true));
            Assert.True(SoundHelper.IsEnabled);
        }

        [Fact]
        public void PlayClick_is_safe_without_a_click_wav_next_to_the_assembly()
        {
            SettingsStore.Set("UiSounds", true);
            SoundHelper.PlayClick();   // must not throw when the player could not be created
        }
    }
}