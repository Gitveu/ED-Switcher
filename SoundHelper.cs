using System;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Collections.Generic;

namespace EDAccountSwitcher.Core
{
    public static class SoundHelper
    {
        private static SoundPlayer _player;

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
        }

        public static void PlayClick()
        {
            try
            {
                bool isSoundEnabled = true; 

                string settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                    if (dict != null && dict.TryGetValue("UiSounds", out object val))
                    {
                        if (val is JsonElement el && (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
                        {
                            isSoundEnabled = el.GetBoolean();
                        }
                    }
                }

                if (isSoundEnabled)
                {
                    _player?.Play();
                }
            }
            catch { }
        }
    }
}