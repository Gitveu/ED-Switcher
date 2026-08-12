using Microsoft.UI.Xaml;
using System;

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
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
        }
    }
}