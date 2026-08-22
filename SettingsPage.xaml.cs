using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;
using L = EDAccountSwitcher.Localization.LocalizationManager;

namespace EDAccountSwitcher
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;

        public const double DefaultAutoExitDelaySeconds = 2.0;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            ShowVersion();
            _isInitializing = false;

            if (InstallPathBox != null)
            {
                ValidateInstallPath(InstallPathBox.Text ?? "");
            }
        }

        // ---------- persistence ----------

        private void SetSetting(string key, object value)
        {
            if (!SettingsStore.Set(key, value))
                ShowSaveError();
        }

        private void ShowSaveError()
        {
            if (SaveErrorBar == null) return;

            SaveErrorBar.Title = L.Get("Settings_SaveFailedTitle");
            SaveErrorBar.Message = L.Format("Settings_SaveFailedMessage",
                                            SettingsStore.FilePath,
                                            SettingsStore.LastError?.Message ?? "");
            SaveErrorBar.IsOpen = true;
        }

        // ---------- loading ----------

        private void LoadSettings()
        {
            if (InstallPathBox != null)
                InstallPathBox.Text = SettingsStore.GetString(
                    "EdInstallPath",
                    @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous") ?? "";

            if (LauncherPathBox != null)
                LauncherPathBox.Text = SettingsStore.GetString(
                    "LauncherPathBox",
                    @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous\MinEdLauncher.exe") ?? "";

            if (HideEmailToggle != null)
                HideEmailToggle.IsOn = SettingsStore.GetBool("HideEmails", false);

            if (SoundToggle != null)
                SoundToggle.IsOn = SettingsStore.GetBool("UiSounds", true);

            if (AutoExitToggle != null)
                AutoExitToggle.IsOn = SettingsStore.GetBool("AutoExit", false);

            if (AutoExitDelayBox != null)
                AutoExitDelayBox.Value = ReadAutoExitDelaySeconds();

            UpdateAutoExitDelayVisibility();

            string savedTheme = SettingsStore.GetString("AppTheme", "Default");
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

            LoadLanguageSetting();
        }

        private void LoadLanguageSetting()
        {
            if (LanguageComboBox == null) return;

            if (SystemLanguageItem != null)
            {
                string detected = L.DetectSystemLanguage() switch
                {
                    "ru" => L.Get("Settings_LanguageRussian"),
                    "be" => L.Get("Settings_LanguageBelarusian"),
                    _ => L.Get("Settings_LanguageEnglish")
                };

                SystemLanguageItem.Content = $"{L.Get("Settings_LanguageSystem")} ({detected})";
            }

            string saved = L.ToSettingValue(L.Selected);

            foreach (var item in LanguageComboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Tag?.ToString(), saved, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageComboBox.SelectedItem = comboItem;
                    break;
                }
            }
        }

        private void ShowVersion()
        {
            if (AboutVersionText == null) return;

            string version = FormatVersion(typeof(App).Assembly.GetName().Version);

            AboutVersionText.Text = version == null
                ? "ED Account Switcher"
                : $"ED Account Switcher {version}";
        }

        private static string FormatVersion(Version version)
        {
            if (version == null) return null;
            if (version.Revision > 0) return version.ToString(4);
            if (version.Build > 0) return version.ToString(3);
            return version.ToString(2);
        }

        private double ReadAutoExitDelaySeconds() =>
            Math.Clamp(SettingsStore.GetDouble("AutoExitDelaySeconds", DefaultAutoExitDelaySeconds), 0, 60);

        private void UpdateAutoExitDelayVisibility()
        {
            if (AutoExitDelayPanel == null || AutoExitToggle == null) return;

            AutoExitDelayPanel.Visibility = AutoExitToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ---------- handlers ----------

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            SoundHelper.PlayClick();

            if (LanguageComboBox.SelectedItem is not ComboBoxItem selectedItem) return;

            AppLanguage language = L.Parse(selectedItem.Tag?.ToString());
            if (language == L.Selected) return;

            SetSetting("AppLanguage", L.ToSettingValue(language));

            // Raises LanguageChanged -> MainWindow refreshes navigation and reloads the current page.
            L.Apply(language);
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

        private void AutoExitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateAutoExitDelayVisibility();

            if (_isInitializing) return;
            SoundHelper.PlayClick();
            SetSetting("AutoExit", AutoExitToggle.IsOn);
        }

        private void AutoExitDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing) return;

            double seconds = double.IsNaN(args.NewValue) ? DefaultAutoExitDelaySeconds : args.NewValue;
            seconds = Math.Clamp(seconds, 0, 60);

            if (double.IsNaN(args.NewValue))
                sender.Value = seconds;

            SetSetting("AutoExitDelaySeconds", seconds);
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

            if (!GameLocator.IsValidInstallDir(InstallPathBox.Text)) return;

            // Both paths in a single file operation.
            bool saved = SettingsStore.SetMany(new Dictionary<string, object>
            {
                ["EdInstallPath"] = InstallPathBox.Text,
                ["LauncherPathBox"] = LauncherPathBox.Text
            });

            if (saved)
            {
                if (SaveErrorBar != null) SaveErrorBar.IsOpen = false;
            }
            else
            {
                ShowSaveError();
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
            filePicker.CommitButtonText = L.Get("Settings_SelectLauncher");

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
                        Title = L.Get("Settings_InvalidExeTitle"),
                        Content = L.Get("Settings_InvalidExeMessage"),
                        CloseButtonText = L.Get("Common_Ok"),
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
        }
    }
}