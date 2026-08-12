using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Core;

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

        private string GetInstallPath()
        {
            try
            {
                string settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null && dict.TryGetValue("EdInstallPath", out object val))
                    {
                        if (val is JsonElement el) return el.GetString();
                        return val?.ToString();
                    }
                }
            }
            catch { }
            return @"C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous";
        }

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
                    ShowError("Please fill in all fields.");
                    return;
                }

                if (_encCode != null && TwoFactorPanel.Visibility == Visibility.Visible)
                {
                    string code = TwoFactorBox.Text.Trim();
                    if (string.IsNullOrEmpty(code))
                    {
                        ShowError("Please enter the verification code.");
                        return;
                    }

                    var tfResult = await _auth.SubmitTwoFactorAsync(_encCode, code);
                    if (tfResult is TwoFactorResult.Success tfSuccess)
                    {
                        SaveAccount(profile, email, password, tfSuccess.MachineToken);
                    }
                    else if (tfResult is TwoFactorResult.Error tfErr)
                    {
                        ShowError($"2FA Error: {tfErr.Message}");
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
                        AuthButton.Content = "Submit Verification Code";
                    }
                    else if (signResult is SignInResult.Error err)
                    {
                        ShowError($"Login Error: {err.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Unexpected error: {ex.Message}");
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
                AuthButton.Content = "Authenticate Account";
                _encCode = null;

                _auth.Dispose();
                _auth = new FrontierAuth(MachineId.GetId());

                ErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                ShowError("Account saved successfully!");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to save .cred file: {ex.Message}");
            }
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}