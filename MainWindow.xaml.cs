using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using EDAccountSwitcher.Core;
using System;
using Microsoft.UI.Windowing;

namespace EDAccountSwitcher
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            this.Title = "ED Switcher";

            try
            {
                this.SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            catch { }

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico");

                if (System.IO.File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch { }

            string savedTheme = "Default";
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                savedTheme = localSettings.Values["AppTheme"]?.ToString() ?? "Default";
            }
            catch
            {
            }

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = savedTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }

        private void NavView_PaneOpening(NavigationView sender, object args)
        {
            AppTitleText.Visibility = Visibility.Visible;
        }

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            AppTitleText.Visibility = Visibility.Collapsed;
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            AppTitleText.Visibility = sender.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(OverviewPage));
            NavView.SelectedItem = NavView.MenuItems[0];
            AppTitleText.Visibility = NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            SoundHelper.PlayClick();

            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                var selectedItem = (NavigationViewItem)args.SelectedItem;
                if (selectedItem != null)
                {
                    string tag = selectedItem.Tag.ToString();
                    switch (tag)
                    {
                        case "OverviewPage":
                            ContentFrame.Navigate(typeof(OverviewPage));
                            break;
                        case "AddAccountPage":
                            ContentFrame.Navigate(typeof(AddAccountPage));
                            break;
                    }
                }
            }
        }
    }
}