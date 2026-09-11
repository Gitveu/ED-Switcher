using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;

using L = EDAccountSwitcher.Localization.LocalizationManager;

namespace EDAccountSwitcher
{
    public sealed partial class OverviewPage : Page
    {
        private const string AccountOrderSetting = "AccountOrder";

        public ObservableCollection<Account> Accounts { get; }
            = new ObservableCollection<Account>();

        public ObservableCollection<InstalledProduct> Products { get; }
            = new ObservableCollection<InstalledProduct>();

        /// <summary>
        /// Не сохраняем выбранный продукт во время первоначальной загрузки.
        /// </summary>
        private bool _isLoadingData;

        public OverviewPage()
        {
            InitializeComponent();

            ApplyToolTips();

            AccountsListView.ItemsSource = Accounts;
            ProductComboBox.ItemsSource = Products;

            LoadData();

            ProductComboBox.SelectionChanged +=
                ProductComboBox_SelectionChanged;

            AppendLog(L.Get("Log_SystemInitialized"));
        }

        private void ApplyToolTips()
        {
            ToolTipService.SetToolTip(
                CopyConsoleButton,
                L.Get("Overview_CopyToClipboard"));

            ToolTipService.SetToolTip(
                SaveConsoleButton,
                L.Get("Overview_SaveToFile"));

            ToolTipService.SetToolTip(
                ClearConsoleButton,
                L.Get("Overview_ClearConsole"));
        }

        private object GetSetting(
            string key,
            object defaultValue = null)
        {
            return SettingsStore.Get(key, defaultValue);
        }

        private void SetSetting(
            string key,
            object value)
        {
            if (!SettingsStore.Set(key, value))
            {
                AppendLog(
                    L.Format(
                        "Log_CouldNotSaveSetting",
                        key,
                        SettingsStore.LastError?.Message ?? ""),
                    true);
            }
        }

        private double GetAutoExitDelaySeconds()
        {
            object raw = GetSetting(
                "AutoExitDelaySeconds",
                SettingsPage.DefaultAutoExitDelaySeconds);

            double seconds = raw switch
            {
                double d => d,
                int i => i,

                string s when double.TryParse(
                    s,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,

                _ => SettingsPage.DefaultAutoExitDelaySeconds
            };

            return Math.Clamp(seconds, 0, 60);
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)
                || !email.Contains("@"))
            {
                return "••••••••••••";
            }

            var parts = email.Split(
                '@',
                2,
                StringSplitOptions.None);

            string name = parts[0];
            string domain = parts[1];

            if (name.Length <= 2)
                return $"•••@{domain}";

            return $"{name[0]}••••••{name[^1]}@{domain}";
        }

        private void LoadData()
        {
            try
            {
                string edPath =
                    GetSetting("EdInstallPath")?.ToString();

                /*
                 * Скрытие e-mail включено по умолчанию.
                 * Сохранённое пользователем значение имеет приоритет.
                 */
                bool hideEmails = SettingsStore.GetBool(
                    "HideEmails",
                    true);

                if (string.IsNullOrWhiteSpace(edPath)
                    || !Directory.Exists(edPath))
                {
                    AppendLog(
                        L.Get("Log_InstallPathNotSet"),
                        true);

                    return;
                }

                var store = CredStore.FromEdInstallDir(edPath);

                var savedAccounts = store
                    .ListAccounts()
                    .ToList();

                ApplySavedAccountOrder(savedAccounts);

                foreach (var account in savedAccounts)
                {
                    string displayEmail = hideEmails
                        ? MaskEmail(account.Email)
                        : account.Email;

                    Accounts.Add(
                        new Account
                        {
                            ProfileName = account.Profile,
                            Email = displayEmail
                        });
                }

                /*
                 * Записываем актуальный порядок после загрузки.
                 * Это также добавляет в настройки новые аккаунты,
                 * которых раньше не было в AccountOrder.
                 */
                SaveAccountOrder();

                if (Accounts.Count > 0)
                    AccountsListView.SelectedIndex = 0;

                var installedProducts =
                    GameLocator.EnumerateProducts(edPath);

                foreach (var product in installedProducts)
                    Products.Add(product);

                _isLoadingData = true;

                try
                {
                    ProductComboBox.SelectedIndex =
                        ResolvePreferredProductIndex();
                }
                finally
                {
                    _isLoadingData = false;
                }

                AppendLog(
                    L.Format(
                        "Log_LoadedProfiles",
                        Accounts.Count,
                        Products.Count));
            }
            catch (Exception ex)
            {
                AppendLog(
                    L.Format(
                        "Log_ErrorLoadingData",
                        ex.Message),
                    true);
            }
        }

        private void ApplySavedAccountOrder(
            List<(string Profile, string Email)> accounts)
        {
            var savedOrder = ReadSavedAccountOrder();

            var savedPositions =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < savedOrder.Count; i++)
            {
                string profile = savedOrder[i];

                if (!savedPositions.ContainsKey(profile))
                    savedPositions.Add(profile, i);
            }

            accounts.Sort((left, right) =>
            {
                bool leftExists = savedPositions.TryGetValue(
                    left.Profile,
                    out int leftPosition);

                bool rightExists = savedPositions.TryGetValue(
                    right.Profile,
                    out int rightPosition);

                /*
                 * Сначала идут аккаунты, которые есть
                 * в сохранённом порядке.
                 */
                if (leftExists && rightExists)
                    return leftPosition.CompareTo(rightPosition);

                if (leftExists)
                    return -1;

                if (rightExists)
                    return 1;

                /*
                 * Новые аккаунты между собой сортируем
                 * так же, как раньше сортировал CredStore.
                 */
                return string.Compare(
                    left.Profile,
                    right.Profile,
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        private List<string> ReadSavedAccountOrder()
        {
            object raw = SettingsStore.Get(
                AccountOrderSetting,
                null);

            if (raw is JsonElement element
                && element.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>();

                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    string value = item.GetString();

                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }

                return result;
            }

            if (raw is IEnumerable<string> values)
            {
                return values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            return new List<string>();
        }

        private void SaveAccountOrder()
        {
            var order = Accounts
                .Select(account => account.ProfileName)
                .Where(profile => !string.IsNullOrWhiteSpace(profile))
                .ToList();

            SetSetting(AccountOrderSetting, order);
        }

        private void MoveAccount(
            Account account,
            int offset)
        {
            if (account == null)
                return;

            int currentIndex =
                Accounts.IndexOf(account);

            if (currentIndex < 0)
                return;

            int newIndex = currentIndex + offset;

            if (newIndex < 0
                || newIndex >= Accounts.Count)
            {
                return;
            }

            Accounts.Move(currentIndex, newIndex);
            AccountsListView.SelectedItem = account;

            SaveAccountOrder();
        }

        private void MoveAccountUp_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (sender is Button button
                && button.Tag is Account account)
            {
                MoveAccount(account, -1);
            }
        }

        private void MoveAccountDown_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (sender is Button button
                && button.Tag is Account account)
            {
                MoveAccount(account, 1);
            }
        }

        private int ResolvePreferredProductIndex()
        {
            if (Products.Count == 0)
                return -1;

            string preferred =
                GetSetting("PreferredVersion")?.ToString();

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                for (int i = 0; i < Products.Count; i++)
                {
                    if (string.Equals(
                            Products[i].Filter,
                            preferred,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog(
                            L.Format(
                                "Log_RestoredVersion",
                                Products[i].Name));

                        return i;
                    }
                }

                AppendLog(
                    L.Format(
                        "Log_SavedVersionMissing",
                        preferred),
                    true);
            }

            return 0;
        }

        private void ProductComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isLoadingData)
                return;

            if (ProductComboBox.SelectedItem
                is not InstalledProduct product)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(product.Filter))
                return;

            SetSetting(
                "PreferredVersion",
                product.Filter);
        }

        private async void DeleteAccount_Click(
            object sender,
            RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            var accountToDelete =
                menuItem?.DataContext as Account;

            if (accountToDelete == null)
                return;

            var dialog = new ContentDialog
            {
                Title = L.Get("Overview_DeleteAccount"),

                Content = L.Format(
                    "Overview_DeleteConfirm",
                    accountToDelete.ProfileName),

                PrimaryButtonText =
                    L.Get("Common_Delete"),

                CloseButtonText =
                    L.Get("Common_Cancel"),

                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            try
            {
                string edPath =
                    GetSetting("EdInstallPath")?.ToString();

                if (string.IsNullOrWhiteSpace(edPath))
                    return;

                var store =
                    CredStore.FromEdInstallDir(edPath);

                store.Delete(
                    store.CredPathForProfile(
                        accountToDelete.ProfileName));

                Accounts.Remove(accountToDelete);

                /*
                 * Важно: после удаления сохраняем новый порядок,
                 * иначе удалённый аккаунт снова появится в AccountOrder.
                 */
                SaveAccountOrder();

                AppendLog(
                    L.Format(
                        "Log_DeletedAccount",
                        accountToDelete.ProfileName));
            }
            catch (Exception ex)
            {
                AppendLog(
                    L.Format(
                        "Log_ErrorDeletingAccount",
                        ex.Message),
                    true);
            }
        }

        private void LaunchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            var selectedAccount =
                AccountsListView.SelectedItem as Account;

            if (selectedAccount == null)
            {
                AppendLog(
                    L.Get("Log_NoAccountSelected"),
                    true);

                return;
            }

            var selectedProduct =
                ProductComboBox.SelectedItem as InstalledProduct;

            if (selectedProduct == null)
            {
                AppendLog(
                    L.Get("Log_NoProductSelected"),
                    true);

                return;
            }

            string launcherPath =
                GetSetting("LauncherPathBox")?.ToString()
                ?? "";

            if (string.IsNullOrWhiteSpace(launcherPath)
                || !File.Exists(launcherPath))
            {
                AppendLog(
                    L.Get("Log_LauncherPathInvalid"),
                    true);

                return;
            }

            bool autoExit =
                SettingsStore.GetBool(
                    "AutoExit",
                    false);

            double autoExitDelay =
                GetAutoExitDelaySeconds();

            /*
             * ProfileName используется для выбора .cred-файла.
             * Email может быть замаскирован, поэтому использовать
             * его для запуска нельзя.
             */
            string args =
                $"/frontier \"{selectedAccount.ProfileName}\" " +
                $"/autorun /{selectedProduct.Filter} /autoquit";

            if (VrModeCheckBox.IsChecked == true)
                args += " /vr";

            AppendLog(
                $"> \"{launcherPath}\" {args}");

            try
            {
                var process = new Process();

                process.StartInfo.FileName =
                    launcherPath;

                process.StartInfo.Arguments =
                    args;

                process.StartInfo.WorkingDirectory =
                    Path.GetDirectoryName(launcherPath);

                process.StartInfo.UseShellExecute =
                    false;

                process.StartInfo.CreateNoWindow =
                    true;

                process.StartInfo.RedirectStandardOutput =
                    true;

                process.StartInfo.RedirectStandardError =
                    true;

                process.StartInfo.EnvironmentVariables.Remove(
                    "DOTNET_STARTUP_HOOKS");

                process.StartInfo.EnvironmentVariables.Remove(
                    "CORECLR_ENABLE_PROFILING");

                process.StartInfo.EnvironmentVariables.Remove(
                    "CORECLR_PROFILER");

                process.StartInfo.EnvironmentVariables.Remove(
                    "CORECLR_PROFILER_PATH");

                process.EnableRaisingEvents = true;

                process.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        AppendLog(ev.Data);
                };

                process.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        AppendLog(ev.Data, true);
                };

                process.Exited += (s, ev) =>
                {
                    int exitCode =
                        process.ExitCode;

                    string status =
                        exitCode == 0
                            ? L.Get("Log_Success")
                            : L.Get("Log_Failure");

                    AppendLog(
                        L.Format(
                            "Log_LauncherExited",
                            exitCode,
                            status));

                    if (!autoExit)
                        return;

                    if (exitCode != 0)
                    {
                        AppendLog(
                            L.Get("Log_AutoExitSkipped"),
                            true);

                        return;
                    }

                    AppendLog(
                        L.Format(
                            "Log_AutoExitClosing",
                            autoExitDelay));

                    _ = ExitAfterDelayAsync(
                        autoExitDelay);
                };

                AppendLog(
                    L.Format(
                        "Log_SpawningProcess",
                        launcherPath));

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                AppendLog(
                    L.Format(
                        "Log_ProcessSpawned",
                        process.Id));
            }
            catch (Exception ex)
            {
                AppendLog(
                    L.Format(
                        "Log_LaunchFailed",
                        ex.Message),
                    true);
            }
        }

        private async Task ExitAfterDelayAsync(
            double delaySeconds)
        {
            if (delaySeconds > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    Application.Current.Exit();
                }
                catch
                {
                    Environment.Exit(0);
                }
            });
        }

        private void AppendLog(
            string message,
            bool isError = false)
        {
            string time =
                DateTime.Now.ToString("HH:mm:ss");

            DispatcherQueue.TryEnqueue(() =>
            {
                string prefix =
                    isError ? "[ERR] " : "";

                LogTextBlock.Text +=
                    $"[{time}] {prefix}{message}\n";

                var scrollViewer =
                    LogTextBlock.Parent as ScrollViewer;

                scrollViewer?.ChangeView(
                    null,
                    scrollViewer.ScrollableHeight,
                    null);
            });
        }

        private void CopyConsole_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (string.IsNullOrEmpty(LogTextBlock.Text))
                return;

            var dataPackage =
                new Windows.ApplicationModel.DataTransfer.DataPackage();

            dataPackage.SetText(LogTextBlock.Text);

            Windows.ApplicationModel.DataTransfer.Clipboard
                .SetContent(dataPackage);

            AppendLog(
                L.Get("Log_ConsoleCopied"));
        }

        private async void SaveConsole_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            if (string.IsNullOrEmpty(LogTextBlock.Text))
                return;

            try
            {
                var savePicker =
                    new Windows.Storage.Pickers.FileSavePicker();

                var window =
                    EDAccountSwitcher.App.MainWindowInstance;

                if (window != null)
                {
                    var hwnd =
                        WinRT.Interop.WindowNative
                            .GetWindowHandle(window);

                    WinRT.Interop.InitializeWithWindow
                        .Initialize(savePicker, hwnd);
                }

                savePicker.SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId
                        .DocumentsLibrary;

                savePicker.FileTypeChoices.Add(
                    L.Get("Overview_TextFileType"),
                    new List<string> { ".txt" });

                savePicker.SuggestedFileName =
                    $"ED_Launcher_Log_{DateTime.Now:yyyyMMdd_HHmmss}";

                Windows.Storage.StorageFile file =
                    await savePicker.PickSaveFileAsync();

                if (file == null)
                    return;

                Windows.Storage.CachedFileManager
                    .DeferUpdates(file);

                await Windows.Storage.FileIO.WriteTextAsync(
                    file,
                    LogTextBlock.Text);

                var status =
                    await Windows.Storage.CachedFileManager
                        .CompleteUpdatesAsync(file);

                if (status ==
                    Windows.Storage.Provider.FileUpdateStatus
                        .Complete)
                {
                    AppendLog(
                        L.Format(
                            "Log_LogSaved",
                            file.Name));
                }
            }
            catch (Exception ex)
            {
                AppendLog(
                    L.Format(
                        "Log_FailedToSaveLog",
                        ex.Message),
                    true);
            }
        }

        private void ClearConsole_Click(
            object sender,
            RoutedEventArgs e)
        {
            SoundHelper.PlayClick();

            LogTextBlock.Text = string.Empty;

            AppendLog(
                L.Get("Log_ConsoleCleared"));
        }
    }

    public sealed class Account
    {
        public string ProfileName { get; set; }
            = string.Empty;

        public string Email { get; set; }
            = string.Empty;
    }
}
