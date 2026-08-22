using System;
using System.IO;
using System.Linq;
using System.Media;

namespace EDAccountSwitcher.Core
{
    public static class SoundHelper
    {
        private static SoundPlayer _player;

        private static bool? _soundsEnabled;

        static SoundHelper()
        {
            try
            {
                string soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "click.wav");

                if (File.Exists(soundPath))
                {
                    _player = new SoundPlayer(soundPath);
                    _player.LoadAsync();
                }
            }
            catch { }

            // The toggle in Settings writes through SettingsStore, so drop the cache when it changes.
            SettingsStore.Changed += keys =>
            {
                if (keys == null || keys.Contains("UiSounds"))
                    _soundsEnabled = null;
            };
        }

        public static bool IsEnabled => (_soundsEnabled ??= SettingsStore.GetBool("UiSounds", true));

        public static void PlayClick()
        {
            try
            {
                if (IsEnabled) _player?.Play();
            }
            catch { }
        }
    }
}