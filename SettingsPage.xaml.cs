using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Core;

namespace EDAccountSwitcher
{
    public sealed partial class SettingsPage : Page
    {
        private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        private Dictionary<string, object> localSettings = new Dictionary<string, object>();
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettingsFromFile();
            LoadSettings();
            _isInitializing = false;

            if (InstallPathBox != null)
            {
                ValidateInstallPath(InstallPathBox.Text ?? "");
            }
        }

        private void LoadSettingsFromFile()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    localSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
            }
            catch { localSettings = new Dictionary<string, object>(); }
        }

        private void SaveSettingsToFile()
        {
            try
            {
                string json = JsonSerializer.Serialize(localSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        private void SetSetting(string key, object value)
        {
            localSettings[key] = value;
            SaveSettingsToFile();
        }

        private object GetSetting(string key, object defaultValue = null)
        {
            if (localSettings.TryGetValue(key, out object value))
            {
                if (value is JsonElement element)
                {
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.String: return element.GetString();
                        case JsonValueKind.True: return true;
                        case JsonValueKind.False: return false;
                        case JsonValueKind.Number: return element.GetDouble();
                    }
                }
                return value;
            }
            return defaultValue;
        }

        private void LoadSettings()
        {
            if (InstallPathBox != null)
                InstallPathBox.Text = GetSetting("EdInstallPath", @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous")?.ToString() ?? "";

            if (LauncherPathBox != null)
                LauncherPathBox.Text = GetSetting("LauncherPathBox", @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous\MinEdLauncher.exe")?.ToString() ?? "";

            if (HideEmailToggle != null)
                HideEmailToggle.IsOn = (GetSetting("HideEmails") as bool?) ?? false;

            if (SoundToggle != null)
                SoundToggle.IsOn = (GetSetting("UiSounds") as bool?) ?? true;

            string savedTheme = GetSetting("AppTheme", "Default")?.ToString();
            if (ThemeRadioButtons != null && ThemeRadioButtons.Items != null)
            {
                foreach (var item in ThemeRadioButtons.Items)
                {
                    if (item is RadioButton rb && rb.Tag != null && rb.Tag.ToString() == savedTheme)
                    {
                        ThemeRadioButtons.SelectedItem = rb;
                        break;
                    }
                }
            }
        }

        private void ThemeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            SoundHelper.PlayClick();

            if (ThemeRadioButtons.SelectedItem is RadioButton selectedRadio)
            {
                string theme = selectedRadio.Tag.ToString();
                SetSetting("AppTheme", theme);

                if (EDAccountSwitcher.App.MainWindowInstance?.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = theme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }

        private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SoundHelper.PlayClick();
            SetSetting("UiSounds", SoundToggle.IsOn);
        }

        private void HideEmailToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SoundHelper.PlayClick();
            SetSetting("HideEmails", HideEmailToggle.IsOn);
        }

        private void InstallPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ValidateInstallPath(InstallPathBox.Text ?? "");
        }

        private void ValidateInstallPath(string path)
        {
            bool isValid = GameLocator.IsValidInstallDir(path);

            if (PathErrorBar != null && SavePathsButton != null)
            {
                if (isValid)
                {
                    PathErrorBar.IsOpen = false;
                    SavePathsButton.IsEnabled = true;
                }
                else
                {
                    PathErrorBar.IsOpen = true;
                    SavePathsButton.IsEnabled = false;
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();
            if (GameLocator.IsValidInstallDir(InstallPathBox.Text))
            {
                SetSetting("EdInstallPath", InstallPathBox.Text);
                SetSetting("LauncherPathBox", LauncherPathBox.Text);
            }
        }

        private async void BrowseInstallPath_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(EDAccountSwitcher.App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                InstallPathBox.Text = folder.Path;
            }
        }

        private async void BrowseLauncherPath_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(EDAccountSwitcher.App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            filePicker.FileTypeFilter.Add(".exe");
            filePicker.CommitButtonText = "Select MinEdLauncher";

            Windows.Storage.StorageFile file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                if (file.Name.Equals("MinEdLauncher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    LauncherPathBox.Text = file.Path;
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Invalid Executable",
                        Content = "You must select the 'MinEdLauncher.exe' file. Other executables are not supported.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
        }
    }
}