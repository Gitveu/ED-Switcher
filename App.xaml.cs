using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;
using EDAccountSwitcher.Updating;

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

            _ = CheckForUpdatesAsync();
        }

        private static async Task CheckForUpdatesAsync()
        {
            await Task.Delay(4000);   // не мешаем первой отрисовке

            for (var i = 0; i < 20 && MainWindowInstance?.Content?.XamlRoot == null; i++)
                await Task.Delay(500);

            var root = MainWindowInstance?.Content?.XamlRoot;
            if (root != null) await UpdateDialog.CheckAsync(root);
        }
    }
}