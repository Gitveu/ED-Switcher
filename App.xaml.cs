using Microsoft.UI.Xaml;
using System;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;

namespace EDAccountSwitcher
{
    public partial class App : Application
    {
        public static Window MainWindowInstance { get; set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Must run before any XAML page is loaded: {loc:Loc} reads the strings at load time.
            LocalizationManager.Initialize(SettingsStore.GetString("AppLanguage", "System"));

            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
        }
    }
}