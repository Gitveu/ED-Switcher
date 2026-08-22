using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;
using L = EDAccountSwitcher.Localization.LocalizationManager;

namespace EDAccountSwitcher
{
    public sealed partial class AddAccountPage : Page
    {
        private string _encCode = null;
        private FrontierAuth _auth;

        public AddAccountPage()
        {
            this.InitializeComponent();

            string machineId = MachineId.GetId();
            _auth = new FrontierAuth(machineId);
        }

        private string GetInstallPath() =>
            SettingsStore.GetString("EdInstallPath",
                @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous");

        private async void AuthButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            AuthButton.IsEnabled = false;

            try
            {
                string profile = ProfileBox.Text.Trim();
                string email = EmailBox.Text.Trim();
                string password = PasswordBox.Password;

                if (string.IsNullOrEmpty(profile) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    ShowMessage(L.Get("AddAccount_FillAllFields"));
                    return;
                }

                if (_encCode != null && TwoFactorPanel.Visibility == Visibility.Visible)
                {
                    string code = TwoFactorBox.Text.Trim();
                    if (string.IsNullOrEmpty(code))
                    {
                        ShowMessage(L.Get("AddAccount_EnterCode"));
                        return;
                    }

                    var tfResult = await _auth.SubmitTwoFactorAsync(_encCode, code);
                    if (tfResult is TwoFactorResult.Success tfSuccess)
                    {
                        SaveAccount(profile, email, password, tfSuccess.MachineToken);
                    }
                    else if (tfResult is TwoFactorResult.Error tfErr)
                    {
                        ShowMessage(L.Format("AddAccount_TwoFactorError", tfErr.Message));
                    }
                }
                else
                {
                    var signResult = await _auth.SignInAsync(email, password);

                    if (signResult is SignInResult.Success success)
                    {
                        SaveAccount(profile, email, password, success.MachineToken);
                    }
                    else if (signResult is SignInResult.RequiresTwoFactor req2fa)
                    {
                        _encCode = req2fa.EncCode;
                        TwoFactorPanel.Visibility = Visibility.Visible;
                        AuthButton.Content = L.Get("AddAccount_SubmitCode");
                    }
                    else if (signResult is SignInResult.Error err)
                    {
                        ShowMessage(L.Format("AddAccount_LoginError", err.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage(L.Format("AddAccount_UnexpectedError", ex.Message));
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                AuthButton.IsEnabled = true;
            }
        }

        private void SaveAccount(string profile, string email, string password, string machineToken)
        {
            try
            {
                string installPath = GetInstallPath();
                var credStore = CredStore.FromEdInstallDir(installPath);
                string credPath = credStore.CredPathForProfile(profile);

                credStore.SaveCredentials(credPath, email, credStore.Encrypt(password), machineToken);

                ProfileBox.Text = "";
                EmailBox.Text = "";
                PasswordBox.Password = "";
                TwoFactorBox.Text = "";
                TwoFactorPanel.Visibility = Visibility.Collapsed;
                AuthButton.Content = L.Get("AddAccount_AuthButton");
                _encCode = null;

                _auth.Dispose();
                _auth = new FrontierAuth(MachineId.GetId());

                ShowMessage(L.Get("AddAccount_Saved"), isSuccess: true);
            }
            catch (Exception ex)
            {
                ShowMessage(L.Format("AddAccount_SaveFailed", ex.Message));
            }
        }

        /// Shows a status line under the title. The colour is set every time, so an
        /// error after a successful save is no longer painted green.
        private void ShowMessage(string message, bool isSuccess = false)
        {
            ErrorText.Foreground = new SolidColorBrush(isSuccess
                ? Microsoft.UI.Colors.LightGreen
                : Microsoft.UI.Colors.OrangeRed);

            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}