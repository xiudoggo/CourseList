using System;
using CourseList;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace CourseList.Views
{
    public sealed partial class MainPage : Page
    {
        private bool _isCompactMode;
        private NavigationViewDisplayMode? _lastDisplayMode;

        private OverlappedPresenter? _overlappedPresenter;
        private OverlappedPresenterState _captionMarginLastState;

        public MainPage()
        {
            this.InitializeComponent();

            ContentFrame.Navigate(typeof(HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];

            ContentFrame.Navigated += ContentFrame_Navigated;
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            SyncContentCompactMode(NavView.DisplayMode);
            RegisterNavigationViewCaptionMarginWorkaround();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnregisterNavigationViewCaptionMarginWorkaround();
        }

        /// <summary>
        /// WinUI Gallery 同款：最大化/还原时微调 NavigationView 顶边距，消除标题栏与内容之间的细缝（见 WinUI-Gallery MainWindow.AdjustNavigationViewMargin）。
        /// </summary>
        private void RegisterNavigationViewCaptionMarginWorkaround()
        {
            if (App.CurrentMainWindow is not Window w)
                return;

            var hwnd = WindowNative.GetWindowHandle(w);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow.Presenter is not OverlappedPresenter op)
                return;

            _overlappedPresenter = op;
            _captionMarginLastState = op.State;
            _appWindowForMargin = appWindow;
            AdjustNavigationViewCaptionMargin(force: true);
            appWindow.Changed += AppWindow_Changed_ForCaptionMargin;
        }

        private AppWindow? _appWindowForMargin;

        private void UnregisterNavigationViewCaptionMarginWorkaround()
        {
            if (_appWindowForMargin != null)
                _appWindowForMargin.Changed -= AppWindow_Changed_ForCaptionMargin;
            _appWindowForMargin = null;
            _overlappedPresenter = null;
        }

        private void AppWindow_Changed_ForCaptionMargin(AppWindow sender, AppWindowChangedEventArgs args)
        {
            AdjustNavigationViewCaptionMargin();
        }

        private void AdjustNavigationViewCaptionMargin(bool force = false)
        {
            if (_overlappedPresenter is null)
                return;

            if (!force && _overlappedPresenter.State == _captionMarginLastState)
                return;

            _captionMarginLastState = _overlappedPresenter.State;
            NavView.Margin = _overlappedPresenter.State == OverlappedPresenterState.Maximized
                ? new Thickness(0, -1, 0, 0)
                : new Thickness(0, -2, 0, 0);
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            ApplyCompactToCurrentContent();
        }

        private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            if (_lastDisplayMode == args.DisplayMode)
                return;
            SyncContentCompactMode(args.DisplayMode);
        }

        private void SyncContentCompactMode(NavigationViewDisplayMode displayMode)
        {
            _lastDisplayMode = displayMode;
            _isCompactMode = displayMode != NavigationViewDisplayMode.Expanded;
            ApplyCompactToCurrentContent();
        }

        private void ApplyCompactToCurrentContent()
        {
            switch (ContentFrame.Content)
            {
                case HomePage hp:
                    hp.ApplyCompactMode(_isCompactMode);
                    break;
                case SchedulePage sp:
                    sp.ApplyCompactMode(_isCompactMode);
                    break;
                case CourseListPage clp:
                    clp.ApplyCompactMode(_isCompactMode);
                    break;
                case SettingsPage spg:
                    spg.ApplyCompactMode(_isCompactMode);
                    break;
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            bool navigated = false;

            if (args.SelectedItem is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString() ?? "";

                Type pageType = tag switch
                {
                    "HomePage" => typeof(HomePage),
                    "SchedulePage" => typeof(SchedulePage),
                    "CourseListPage" => typeof(CourseListPage),
                    "SettingsPage" => typeof(SettingsPage),
                    _ => typeof(HomePage)
                };

                if (ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                    navigated = true;
                }
            }

            if (sender.DisplayMode != NavigationViewDisplayMode.Expanded)
                sender.IsPaneOpen = false;

            // 已 Navigate 时由 ContentFrame_Navigated 统一 ApplyCompact，避免重复布局
            if (!navigated)
                ApplyCompactToCurrentContent();
        }

        public void TogglePaneFromTitleBar()
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        public void ShowHomePage()
        {
            if (NavView?.MenuItems?.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
                return;
            }

            if (ContentFrame != null && ContentFrame.CurrentSourcePageType != typeof(HomePage))
                ContentFrame.Navigate(typeof(HomePage));
        }

        public void RefreshCloseOptionsIfSettingsPageVisible()
        {
            if (ContentFrame?.Content is SettingsPage sp)
                sp.RefreshCloseOptionsFromConfig();
        }
    }
}
