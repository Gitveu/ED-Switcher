using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Core;

namespace EDAccountSwitcher
{
    public sealed partial class OverviewPage : Page
    {
        public ObservableCollection<Account> Accounts { get; set; }
        public ObservableCollection<InstalledProduct> Products { get; set; }

        /// Suppresses persistence while LoadData() restores the saved selection.
        private bool _isLoadingData;

        public OverviewPage()
        {
            this.InitializeComponent();

            Accounts = new ObservableCollection<Account>();
            AccountsListView.ItemsSource = Accounts;

            Products = new ObservableCollection<InstalledProduct>();
            ProductComboBox.ItemsSource = Products;

            LoadData();

            // Subscribed after LoadData() so restoring the saved selection isn't treated as a user choice.
            ProductComboBox.SelectionChanged += ProductComboBox_SelectionChanged;

            AppendLog("System initialized. Ready.");
        }

        private object GetSetting(string key, object defaultValue = null)
        {
            try
            {
                string settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null && dict.TryGetValue(key, out object value))
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
                }
            }
            catch { }
            return defaultValue;
        }

        /// Writes a single key into settings.json, preserving everything else that is already there.
        private void SetSetting(string key, object value)
        {
            try
            {
                string settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");

                Dictionary<string, object> dict = null;
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }
                dict ??= new Dictionary<string, object>();

                dict[key] = value;

                File.WriteAllText(settingsFile,
                    JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                AppendLog($"Could not save setting '{key}': {ex.Message}", true);
            }
        }

        /// Reads the Auto Exit grace period (seconds) written by SettingsPage.
        private double GetAutoExitDelaySeconds()
        {
            object raw = GetSetting("AutoExitDelaySeconds", SettingsPage.DefaultAutoExitDelaySeconds);

            double seconds = raw switch
            {
                double d => d,
                int i => i,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => SettingsPage.DefaultAutoExitDelaySeconds
            };

            return Math.Clamp(seconds, 0, 60);
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@")) return "••••••••••••";
            var parts = email.Split('@');
            string name = parts[0];
            string domain = parts[1];
            if (name.Length <= 2) return $"•••@{domain}";
            return $"{name[0]}••••••{name[name.Length - 1]}@{domain}";
        }

        private void LoadData()
        {
            try
            {
                string edPath = GetSetting("EdInstallPath")?.ToString();
                bool hideEmails = (GetSetting("HideEmails") as bool?) ?? false;

                if (!string.IsNullOrEmpty(edPath) && Directory.Exists(edPath))
                {
                    var store = CredStore.FromEdInstallDir(edPath);
                    var savedAccounts = store.ListAccounts();
                    foreach (var acc in savedAccounts)
                    {
                        string displayEmail = hideEmails ? MaskEmail(acc.Email) : acc.Email;
                        Accounts.Add(new Account { ProfileName = acc.Profile, Email = displayEmail });
                    }
                    if (Accounts.Count > 0) AccountsListView.SelectedIndex = 0;

                    var installedProducts = GameLocator.EnumerateProducts(edPath);
                    foreach (var prod in installedProducts)
                    {
                        Products.Add(prod);
                    }

                    _isLoadingData = true;
                    try
                    {
                        ProductComboBox.SelectedIndex = ResolvePreferredProductIndex();
                    }
                    finally
                    {
                        _isLoadingData = false;
                    }

                    AppendLog($"Loaded {Accounts.Count} profile(s) and {Products.Count} installed product(s).");
                }
                else
                {
                    AppendLog("Warning: ED Install Path is not set. Go to Settings first.", true);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error loading data: {ex.Message}", true);
            }
        }

        /// Picks the product saved as "PreferredVersion" (stored as the launcher filter, e.g. "edo"),
        /// falling back to the first installed product.
        private int ResolvePreferredProductIndex()
        {
            if (Products.Count == 0) return -1;

            string preferred = GetSetting("PreferredVersion")?.ToString();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                for (int i = 0; i < Products.Count; i++)
                {
                    if (string.Equals(Products[i].Filter, preferred, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog($"Restored last used version: {Products[i].Name}");
                        return i;
                    }
                }

                AppendLog($"Saved version '{preferred}' is no longer installed. Using the first product instead.", true);
            }

            return 0;
        }

        private void ProductComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingData) return;

            if (ProductComboBox.SelectedItem is not InstalledProduct product) return;
            if (string.IsNullOrWhiteSpace(product.Filter)) return;

            SetSetting("PreferredVersion", product.Filter);
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            var accountToDelete = menuItem?.DataContext as Account;

            if (accountToDelete == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Account",
                Content = $"Are you sure you want to delete credentials for '{accountToDelete.ProfileName}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                string edPath = GetSetting("EdInstallPath")?.ToString();

                if (string.IsNullOrEmpty(edPath)) return;

                var store = CredStore.FromEdInstallDir(edPath);
                store.Delete(store.CredPathForProfile(accountToDelete.ProfileName));
                Accounts.Remove(accountToDelete);

                AppendLog($"Deleted account: {accountToDelete.ProfileName}");
            }
            catch (Exception ex)
            {
                AppendLog($"Error deleting account: {ex.Message}", true);
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            var selectedAccount = AccountsListView.SelectedItem as Account;
            if (selectedAccount == null)
            {
                AppendLog("Error: No account selected!", true);
                return;
            }

            var selectedProduct = ProductComboBox.SelectedItem as InstalledProduct;
            if (selectedProduct == null)
            {
                AppendLog("Error: No product selected to launch!", true);
                return;
            }

            string launcherPath = GetSetting("LauncherPathBox")?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                AppendLog("Error: MinEdLauncher.exe path is invalid or not set in Settings!", true);
                return;
            }

            // Snapshot the Auto Exit settings at launch time so changing them mid-session has no effect.
            bool autoExit = (GetSetting("AutoExit") as bool?) ?? false;
            double autoExitDelay = GetAutoExitDelaySeconds();

            string args = $"/frontier \"{selectedAccount.ProfileName}\" /autorun /{selectedProduct.Filter} /autoquit";

            if (VrModeCheckBox.IsChecked == true)
            {
                args += " /vr";
            }

            AppendLog($"> \"{launcherPath}\" {args}");

            try
            {
                Process process = new Process();
                process.StartInfo.FileName = launcherPath;
                process.StartInfo.Arguments = args;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(launcherPath);

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.StartInfo.EnvironmentVariables.Remove("DOTNET_STARTUP_HOOKS");
                process.StartInfo.EnvironmentVariables.Remove("CORECLR_ENABLE_PROFILING");
                process.StartInfo.EnvironmentVariables.Remove("CORECLR_PROFILER");
                process.StartInfo.EnvironmentVariables.Remove("CORECLR_PROFILER_PATH");

                process.EnableRaisingEvents = true;

                process.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendLog(ev.Data);
                };

                process.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendLog(ev.Data, true);
                };

                process.Exited += (s, ev) =>
                {
                    int exitCode = process.ExitCode;
                    string status = exitCode == 0 ? "(Success)" : "(Failure)";
                    AppendLog($"Launcher process exited with code {exitCode} {status}.");

                    // MinEdLauncher quits once the game is up (/autoquit), so its exit is our "game started" signal.
                    if (!autoExit) return;

                    if (exitCode != 0)
                    {
                        AppendLog("Auto Exit skipped: launcher reported a failure, keeping the window open.", true);
                        return;
                    }

                    AppendLog($"Auto Exit: closing ED Switcher in {autoExitDelay:0.#}s...");
                    _ = ExitAfterDelayAsync(autoExitDelay);
                };

                AppendLog($"Spawning process: {launcherPath}");
                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                AppendLog($"[SUCCESS] Process spawned (PID: {process.Id}). Streaming output...");
            }
            catch (Exception ex)
            {
                AppendLog($"Launch failed: {ex.Message}", true);
            }
        }

        /// Shuts the app down after a short grace period, on the UI thread.
        private async Task ExitAfterDelayAsync(double delaySeconds)
        {
            if (delaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            DispatcherQueue.TryEnqueue(() =>
            {
                try { Application.Current.Exit(); }
                catch { Environment.Exit(0); }
            });
        }

        private void AppendLog(string message, bool isError = false)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            DispatcherQueue.TryEnqueue(() =>
            {
                string prefix = isError ? "[ERR] " : "";
                LogTextBlock.Text += $"[{time}] {prefix}{message}\n";

                var scrollViewer = LogTextBlock.Parent as ScrollViewer;
                scrollViewer?.ChangeView(null,scrollViewer.ScrollableHeight, null);
            });
        }

        private void CopyConsole_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (string.IsNullOrEmpty(LogTextBlock.Text)) return;

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(LogTextBlock.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            AppendLog("Console output copied to clipboard.");
        }

        private async void SaveConsole_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (string.IsNullOrEmpty(LogTextBlock.Text)) return;

            try
            {
                var savePicker = new Windows.Storage.Pickers.FileSavePicker();

                var window = EDAccountSwitcher.App.MainWindowInstance;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                }

                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Text File", new List<string>() { ".txt" });
                savePicker.SuggestedFileName = $"ED_Launcher_Log_{DateTime.Now:yyyyMMdd_HHmmss}";

                Windows.Storage.StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    Windows.Storage.CachedFileManager.DeferUpdates(file);
                    await Windows.Storage.FileIO.WriteTextAsync(file, LogTextBlock.Text);
                    var status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        AppendLog($"Log saved to {file.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to save log: {ex.Message}", true);
            }
        }

        private void ClearConsole_Click(object sender, RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            LogTextBlock.Text = string.Empty;
            AppendLog("Console cleared.");
        }
    }

    public class Account
    {
        public string ProfileName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}