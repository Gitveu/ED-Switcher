using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;
using EDAccountSwitcher.Updating;
using L = EDAccountSwitcher.Localization.LocalizationManager;

namespace EDAccountSwitcher
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;
        private List<(string Code, string DisplayName)> _gameLanguages = new();

        public const double DefaultAutoExitDelaySeconds = 2.0;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            LoadGameLanguage();
            ShowVersion();
            InitUpdateCard();
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
            SaveErrorBar.Visibility = Visibility.Visible;
            SaveErrorBar.IsOpen = true;
        }

        private void InfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            sender.Visibility = Visibility.Collapsed;
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

        private void LoadGameLanguage()
        {
            if (GameLanguageComboBox == null) return;

            string installPath = SettingsStore.GetString("EdInstallPath", "");
            _gameLanguages = GameLanguage.GetAvailableLanguages(installPath).ToList();
            string currentLanguage = MinEdLauncherSettings.GetLanguage();

            GameLanguageComboBox.ItemsSource = _gameLanguages.Select(l =>
            {
                string name = l.Code == null ? L.Get("Settings_GameLanguageDefault") : l.DisplayName;
                return $"{name}{(l.Code != null ? $" ({l.Code})" : "")}";
            }).ToList();

            int selectedIndex = _gameLanguages.FindIndex(l =>
                (l.Code == null && currentLanguage == null) ||
                string.Equals(l.Code, currentLanguage, StringComparison.OrdinalIgnoreCase));

            GameLanguageComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
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

        private void GameLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (GameLanguageComboBox.SelectedIndex >= 0 &&
                GameLanguageComboBox.SelectedIndex < _gameLanguages.Count)
            {
                string? newLanguage = _gameLanguages[GameLanguageComboBox.SelectedIndex].Code;
                MinEdLauncherSettings.SetLanguage(newLanguage);
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            SoundHelper.PlayClick();

            if (LanguageComboBox.SelectedItem is not ComboBoxItem selectedItem) return;

            AppLanguage language = L.Parse(selectedItem.Tag?.ToString());
            if (language == L.Selected) return;

            SetSetting("AppLanguage", L.ToSettingValue(language));

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

        private DispatcherTimer? _saveSuccessTimer;

        private void ShowSaveSuccess()
        {
            if (SaveSuccessBar == null) return;

            SaveSuccessBar.Visibility = Visibility.Visible;
            SaveSuccessBar.IsOpen = true;

            _saveSuccessTimer?.Stop();
            _saveSuccessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _saveSuccessTimer.Tick += (s, e) =>
            {
                _saveSuccessTimer?.Stop();
                if (SaveSuccessBar != null)
                {
                    SaveSuccessBar.IsOpen = false;
                    SaveSuccessBar.Visibility = Visibility.Collapsed;
                }
            };
            _saveSuccessTimer.Start();
        }

        private void HideSaveSuccess()
        {
            _saveSuccessTimer?.Stop();
            if (SaveSuccessBar != null && SaveSuccessBar.IsOpen)
            {
                SaveSuccessBar.IsOpen = false;
                SaveSuccessBar.Visibility = Visibility.Collapsed;
            }
        }

        private void InstallPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            HideSaveSuccess();
            ValidateInstallPath(InstallPathBox.Text ?? "");
        }

        private void LauncherPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            HideSaveSuccess();
        }

        private void ValidateInstallPath(string path)
        {
            bool isValid = GameLocator.IsValidInstallDir(path);

            if (PathErrorBar != null && SavePathsButton != null)
            {
                if (isValid)
                {
                    PathErrorBar.IsOpen = false;
                    PathErrorBar.Visibility = Visibility.Collapsed;
                    SavePathsButton.IsEnabled = true;
                }
                else
                {
                    PathErrorBar.Visibility = Visibility.Visible;
                    PathErrorBar.IsOpen = true;
                    SavePathsButton.IsEnabled = false;
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (!GameLocator.IsValidInstallDir(InstallPathBox.Text)) return;

            bool saved = SettingsStore.SetMany(new Dictionary<string, object>
            {
                ["EdInstallPath"] = InstallPathBox.Text,
                ["LauncherPathBox"] = LauncherPathBox.Text
            });

            if (saved)
            {
                if (SaveErrorBar != null)
                {
                    SaveErrorBar.IsOpen = false;
                    SaveErrorBar.Visibility = Visibility.Collapsed;
                }
                ShowSaveSuccess();
                LoadGameLanguage();
            }
            else
            {
                HideSaveSuccess();
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

        // ---------- updates ----------

        private void InitUpdateCard()
        {
            if (UpdateVersionText != null)
                UpdateVersionText.Text = UpdateService.CurrentTag;
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            CheckUpdatesButton.IsEnabled = false;
            SetUpdateStatus(null, L.Get("Update_Checking"));

            var result = await UpdateDialog.CheckAsync(this.Content.XamlRoot, manual: true);

            switch (result.State)
            {
                case UpdateCheckState.UpToDate:
                    SetUpdateStatus(UpdateOkIcon, L.Get("Update_StatusUpToDate"));
                    ShowUpdateBanner(InfoBarSeverity.Success, L.Get("Update_UpToDate"), null);
                    break;

                case UpdateCheckState.Available:
                    SetUpdateStatus(UpdateAvailableIcon, L.Get("Update_StatusAvailable"));
                    ShowUpdateBanner(InfoBarSeverity.Informational,
                        L.Format("Update_BannerAvailable", result.Info.Tag), null);
                    break;

                default:
                    SetUpdateStatus(UpdateFailedIcon, L.Get("Update_StatusFailed"));
                    ShowUpdateBanner(InfoBarSeverity.Error, L.Get("Update_Failed"), result.Error);
                    break;
            }

            CheckUpdatesButton.IsEnabled = true;
        }

        private void SetUpdateStatus(FontIcon icon, string text)
        {
            UpdateOkIcon.Visibility = Visibility.Collapsed;
            UpdateAvailableIcon.Visibility = Visibility.Collapsed;
            UpdateFailedIcon.Visibility = Visibility.Collapsed;

            if (icon != null)
                icon.Visibility = Visibility.Visible;

            // цвет текста берём у иконки или у подписи версии: кисти заданы в XAML,
            // искать их по ключу в рантайме не нужно
            UpdateStatusText.Foreground = icon != null ? icon.Foreground : UpdateVersionText.Foreground;
            UpdateStatusText.Text = text;
        }

        private void ShowUpdateBanner(InfoBarSeverity severity, string title, string message)
        {
            UpdateInfoBar.Severity = severity;
            UpdateInfoBar.Title = title;
            UpdateInfoBar.Message = message ?? "";
            UpdateInfoBar.Visibility = Visibility.Visible;

            // подписка до IsOpen: раскрытие меняет высоту, и прокрутку надо делать
            // именно по факту этого изменения, а не сразу после установки флага
            UpdateInfoBar.SizeChanged -= UpdateInfoBar_SizeChanged;
            UpdateInfoBar.SizeChanged += UpdateInfoBar_SizeChanged;

            // и ещё одна попытка после следующего кадра — на случай, когда высота
            // не изменилась (тот же текст второй раз) и SizeChanged не сработает
            DispatcherQueue.TryEnqueue(ScrollUpdateBarIntoView);

            UpdateInfoBar.IsOpen = true;
        }

        private void UpdateInfoBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateInfoBar.SizeChanged -= UpdateInfoBar_SizeChanged;   // одна прокрутка на показ
            ScrollUpdateBarIntoView();
        }

        // Прокрутка страницы к результату проверки.
        private void ScrollUpdateBarIntoView()
        {
            if (this.Content is not ScrollViewer scroll) return;

            // без UpdateLayout в ScrollableHeight ещё нет высоты раскрытого баннера
            UpdateLayout();
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);

            // и повторно после кадра: extent содержимого догоняет не мгновенно
            DispatcherQueue.TryEnqueue(() =>
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true));
        }
    }
}