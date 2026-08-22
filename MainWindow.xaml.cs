using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using EDAccountSwitcher.Core;
using EDAccountSwitcher.Localization;
using System;
using Microsoft.UI.Windowing;
using L = EDAccountSwitcher.Localization.LocalizationManager;

namespace EDAccountSwitcher
{
    public sealed partial class MainWindow : Window
    {
        ///  Frame hosting the pages. Replaced on language change to drop cached pages. 
        private Frame _activeFrame;

        public MainWindow()
        {
            this.InitializeComponent();

            _activeFrame = ContentFrame;

            this.Title = L.Get("App_Title");

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

            // settings.json is the real store for an unpackaged app (ApplicationData is unavailable here).
            string savedTheme = SettingsStore.GetString("AppTheme", "Default");

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = savedTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }

            L.LanguageChanged += OnLanguageChanged;
            this.Closed += (s, e) => L.LanguageChanged -= OnLanguageChanged;

            ApplyLocalization();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                ApplyLocalization();
                RecreateContentFrame();
            });
        }

        ///  Refreshes strings owned by this window: title bar and navigation items. 
        private void ApplyLocalization()
        {
            this.Title = L.Get("App_Title");
            AppTitleText.Text = L.Get("App_Title");

            foreach (object item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string tag)
                {
                    switch (tag)
                    {
                        case "OverviewPage":
                            navItem.Content = L.Get("Nav_Overview");
                            break;
                        case "AddAccountPage":
                            navItem.Content = L.Get("Nav_AddAccount");
                            break;
                    }
                }
            }

            // SettingsItem exists only after the NavigationView template has been applied.
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = L.Get("Nav_Settings");
            }
        }

        ///  
        /// {loc:Loc} is evaluated when a page is loaded, and AddAccountPage uses
        /// NavigationCacheMode="Required", so a brand new Frame is the reliable way to
        /// re-render every page in the new language without restarting the app.
        ///  
        private void RecreateContentFrame()
        {
            Type currentPage = _activeFrame?.CurrentSourcePageType ?? typeof(OverviewPage);

            var freshFrame = new Frame
            {
                Padding = _activeFrame?.Padding ?? new Thickness(24),
                Margin = _activeFrame?.Margin ?? new Thickness(0),
                HorizontalAlignment = _activeFrame?.HorizontalAlignment ?? HorizontalAlignment.Stretch,
                VerticalAlignment = _activeFrame?.VerticalAlignment ?? VerticalAlignment.Stretch
            };

            _activeFrame = freshFrame;
            NavView.Content = freshFrame;
            freshFrame.Navigate(currentPage);
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
            ApplyLocalization();   // NavView.SettingsItem is available by now

            _activeFrame.Navigate(typeof(OverviewPage));
            NavView.SelectedItem = NavView.MenuItems[0];
            AppTitleText.Visibility = NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            SoundHelper.PlayClick();

            if (args.IsSettingsSelected)
            {
                _activeFrame.Navigate(typeof(SettingsPage));
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
                            _activeFrame.Navigate(typeof(OverviewPage));
                            break;
                        case "AddAccountPage":
                            _activeFrame.Navigate(typeof(AddAccountPage));
                            break;
                    }
                }
            }
        }
    }
}