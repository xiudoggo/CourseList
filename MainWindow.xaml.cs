using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using CourseList.Views;
using WinRT.Interop;
using CourseList.Helpers;
using CourseList.Helpers.Platform;
using CourseList.Helpers.Ui;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Threading.Tasks;
using System.IO;



// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CourseList
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const uint TrayCallbackMessage = 0x8001; // WM_APP + 1

        private const double MinWindowWidth = 450;

        // 依据当前 NavigationView 的 compact/expanded 阈值（MainPage 里为 520/768）
        private const double CompactWidthDip = MinWindowWidth;   // 小窗口固定到最小拖拽宽度
        private const double ExpandedWidthDip = 1008; // 大窗口固定一个合适宽度
        private const double ExpandedModeThresholdWidthDip = 768;

        private IntPtr _hwnd;
        private IntPtr _oldWndProcPtr;
        private WndProcDelegate? _wndProcDelegate;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private TrayMenuController? _trayIcon;
        private AppWindow? _appWindow;

        private bool _trayExitRequested;

        private bool _closePromptInProgress;
        private bool _directCloseRequested;
        private WindowModeController? _windowModeController;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;

            var hwnd = WindowNative.GetWindowHandle(this);
            _hwnd = hwnd;

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += AppWindow_Closing;
            TryApplyWindowIcon();

            // 标准高度标题栏：与内置 PaneToggle 尺寸一致；先设 PreferredHeightOption，再 SetTitleBar
            ApplyStandardTitleBarChrome();

            SetTitleBar(AppTitleBar);
            // When enabled, "closing" will hide window to system tray instead of exiting.
            this.Closed += MainWindow_Closed;

            // Hook WM_NCLBUTTONDBLCLK to prevent compact-mode caption double-click maximizing.
            // This avoids rapid clicking the title-bar adjacent button being interpreted as a caption double-click.
            _wndProcDelegate = WndProcHook;
            _oldWndProcPtr = WindowInteropHelper.SetWindowLongPtr(_hwnd, WindowInteropHelper.GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            // Initialize tray icon based on current settings.
            var config = ConfigHelper.LoadConfig();
            bool minimizeToTray = config.MinimizeToTrayOnClose;

            _trayIcon = new TrayMenuController(
                hwnd: hwnd,
                callbackMessage: TrayCallbackMessage,
                // 托盘图标始终创建并可见；关闭行为由 MinimizeToTrayOnClose 决定
                startHidden: false,
                tooltip: "CourseList",
                onRestoreRequested: RestoreFromTray,
                onShowExpandedRequested: ShowExpandedFromTrayMenu,
                onShowCompactRequested: ShowCompactFromTrayMenu,
                onExitRequested: ExitFromTray);

            UpdateTrayCloseBehavior(minimizeToTray);

            // Initialize custom title-bar controls.
            try
            {
                PinTopToggleBtn.IsChecked = false;
                SetAlwaysOnTop(false);
                PinSymbol.Symbol = Symbol.Pin;

                _windowModeController = new WindowModeController(
                    hwnd: _hwnd,
                    appWindow: _appWindow!,
                    toggleButton: WindowModeToggleBtn,
                    iconSmall: WindowModeIconSmall,
                    iconLarge: WindowModeIconLarge,
                    brandTitleText: BrandTitleText,
                    minWindowWidthDip: MinWindowWidth,
                    compactWidthDip: CompactWidthDip,
                    expandedWidthDip: ExpandedWidthDip,
                    expandedModeThresholdWidthDip: ExpandedModeThresholdWidthDip);
                _windowModeController.UpdateToggleTip();
                _appWindow!.Changed += AppWindow_Changed_ForWindowModeTip;
            }
            catch
            {
                // ignore
            }

        }

        private void AppWindow_Changed_ForWindowModeTip(object sender, AppWindowChangedEventArgs args)
        {
            _windowModeController?.UpdateToggleTip();
        }

        private void SetAlwaysOnTop(bool alwaysOnTop)
        {
            try
            {
                if (_appWindow?.Presenter is OverlappedPresenter presenter)
                    presenter.IsAlwaysOnTop = alwaysOnTop;
            }
            catch
            {
                // ignore
            }
        }

        private void PinTopToggleBtn_Checked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SetAlwaysOnTop(true);
            PinSymbol.Symbol = Symbol.UnPin;
            ToolTipService.SetToolTip(PinTopToggleBtn, "取消置顶");
        }

        private void PinTopToggleBtn_Unchecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SetAlwaysOnTop(false);
            PinSymbol.Symbol = Symbol.Pin;
            ToolTipService.SetToolTip(PinTopToggleBtn, "置顶");
        }

        private async void WindowModeToggleBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_windowModeController == null)
                return;

            await _windowModeController.ToggleAsync();
        }

        private void ApplyStandardTitleBarChrome()
        {
            if (_appWindow == null)
                return;
            try
            {
                // 让最小化/最大化/关闭等系统 caption buttons 更大
                _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

                // 避免 TitleBar 默认窗口图标影响你要的“无标题”视觉
                _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            }
            catch
            {
                // 忽略兼容性异常
            }
        }

        private void TryApplyWindowIcon()
        {
            if (_appWindow == null)
                return;
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "CourseList.ico");
                if (File.Exists(iconPath))
                    _appWindow.SetIcon(iconPath);
            }
            catch
            {
                // ignore and fallback to default icon
            }
        }

        private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WindowInteropHelper.WmGetMinMaxInfo)
                {
                    // 通过 Win32 获取最小拖拽尺寸，防止窗口缩到过窄后“回闪到最小宽度”。
                    // 该方式不做 Move/Resize，不干预后续放大/缩小手感。
                    WindowInteropHelper.SetMinTrackWidthFromDip(hWnd, lParam, MinWindowWidth);
                }

                if (msg == WindowInteropHelper.WmNcLButtonDblClk)
                {
                    // Block caption double-click maximize globally to avoid title-bar button click conflicts.
                    if (wParam.ToInt32() == WindowInteropHelper.HtCaption)
                        return IntPtr.Zero;
                }
            }
            catch
            {
                // ignore
            }

            return WindowInteropHelper.CallWindowProc(_oldWndProcPtr, hWnd, msg, wParam, lParam);
        }

        private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            if (RootFrame?.Content is MainPage mp)
                mp.TogglePaneFromTitleBar();
        }

        private void NavigateToHomePage()
        {
            if (RootFrame?.Content is MainPage mp)
            {
                mp.ShowHomePage();
                return;
            }

            // 兜底：如果 Frame 尚未创建/内容类型不匹配，尝试导航后再切回首页。
            try
            {
                RootFrame?.Navigate(typeof(MainPage));
            }
            catch
            {
                // ignore
            }

            if (RootFrame?.Content is MainPage mp2)
            {
                mp2.ShowHomePage();
            }
        }

        private void ShowExpandedFromTrayMenu()
        {
            if (_trayExitRequested)
                return;

            if (_trayIcon?.IsHiddenToTray == true)
            {
                RestoreFromTray();
            }
            else
            {
                if (_appWindow != null)
                    _trayIcon?.EnsureWindowShown(_appWindow);
                Activate();
            }

            _ = _windowModeController?.SetExpandedAsync();
        }

        private void ShowCompactFromTrayMenu()
        {
            if (_trayExitRequested)
                return;

            if (_trayIcon?.IsHiddenToTray == true)
            {
                RestoreFromTray();
            }
            else
            {
                if (_appWindow != null)
                    _trayIcon?.EnsureWindowShown(_appWindow);
                Activate();
            }

            _ = _windowModeController?.SetCompactAsync();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // If user explicitly chose "exit" from tray or direct-close, allow the app to close.
            if (_trayExitRequested || _directCloseRequested)
                return;

            var config = ConfigHelper.LoadConfig();

            // If prompt is disabled, keep old behavior (MinimizeToTrayOnClose decides).
            if (!config.ClosePromptEnabled)
            {
                if (!config.MinimizeToTrayOnClose)
                    return;

                args.Cancel = true;
                HideToTrayInternal();
                return;
            }

            // Prompt enabled: cancel this close request and show the dialog asynchronously.
            if (_closePromptInProgress)
            {
                args.Cancel = true;
                return;
            }

            args.Cancel = true;
            _closePromptInProgress = true;
            _ = ShowClosePromptAsync();
        }

        private void HideToTrayInternal()
        {
            if (_appWindow == null || _trayIcon == null)
                return;
            _trayIcon.HideToTray(_appWindow);
        }

        private async Task ShowClosePromptAsync()
        {
            try
            {
                var config = ConfigHelper.LoadConfig();
                var dialogResult = await CloseBehaviorDialog.ShowAsync(
                    xamlRoot: this.Content?.XamlRoot ?? RootFrame?.XamlRoot,
                    requestedTheme: (RootFrame as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
                    initialMinimizeToTray: config.MinimizeToTrayOnClose);
                if (!dialogResult.Confirmed)
                    return;
                bool chooseTray = dialogResult.MinimizeToTray;
                bool dontRemind = dialogResult.DontRemindAgain;

                // Persist chosen behavior so future closing matches the last choice.
                config.MinimizeToTrayOnClose = chooseTray;
                if (dontRemind)
                    config.ClosePromptEnabled = false;

                // Synchronous save ensures SettingsPage immediately sees the updated value.
                ConfigHelper.SaveConfig(config);

                if (chooseTray)
                {
                    HideToTrayInternal();
                }
                else
                {
                    // Allow closing without prompting again.
                    _directCloseRequested = true;
                    try
                    {
                        _trayIcon?.SetHidden(true);
                    }
                    catch
                    {
                        // ignore
                    }

                    Close();
                }
            }
            finally
            {
                _closePromptInProgress = false;
            }
        }

        private void RestoreFromTray()
        {
            if (_trayExitRequested)
                return;

            if (_appWindow != null)
                _trayIcon?.RestoreFromTray(_appWindow);

            // 注意：RootFrame.Content 永远是 MainPage；
            // SettingsPage 实际显示在 MainPage 内部的 ContentFrame。
            if (RootFrame?.Content is MainPage mp)
            {
                mp.RefreshCloseOptionsIfSettingsPageVisible();
            }

            Activate();
        }

        public void BringToFrontFromActivation()
        {
            if (_trayExitRequested)
                return;

            if (_trayIcon?.IsHiddenToTray == true)
            {
                RestoreFromTray();
                return;
            }

            if (_appWindow != null)
                _trayIcon?.EnsureWindowShown(_appWindow);

            Activate();
        }

        private void ExitFromTray()
        {
            if (_trayExitRequested)
                return;

            _trayExitRequested = true;

            try
            {
                // 不要在这里 Dispose：避免和托盘菜单/窗口关闭消息重入导致崩溃。
                // 统一在 MainWindow_Closed 里释放托盘资源。
                _trayIcon?.SetHidden(true);
            }
            catch
            {
                // ignore
            }

            // Triggers AppWindow.Closing again, but _trayExitRequested prevents cancel.
            Close();
        }

        public void UpdateTrayCloseBehavior(bool minimizeToTray)
        {
            if (_trayIcon == null)
                return;

            // 托盘图标始终可见；仅关闭行为（X按钮）由 minimizeToTray 控制。
            _trayIcon.SetHidden(false);

            // Avoid a potential "hidden but icon disabled" lockout.
            if (_trayIcon.IsHiddenToTray && _appWindow != null)
            {
                _trayIcon.RestoreFromTray(_appWindow);
                Activate();
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            try
            {
                // Ensure torn-out pin windows don't keep process alive.
                TodoPinWindowManager.CloseAllPinWindows();

                _windowModeController?.Dispose();
                _windowModeController = null;

                _trayIcon?.Dispose();
                _trayIcon = null;
            }
            catch
            {
                // ignore
            }
        }
    }
}
