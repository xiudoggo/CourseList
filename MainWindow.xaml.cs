using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CourseList.Views;
using WinRT.Interop;
using CourseList.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Threading.Tasks;



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

        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int HTCAPTION = 0x0002;
        private const int GWLP_WNDPROC = -4;
        private const int WM_GETMINMAXINFO = 0x0024;

        private IntPtr _hwnd;
        private IntPtr _oldWndProcPtr;
        private WndProcDelegate? _wndProcDelegate;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private Win32TrayIcon? _trayIcon;
        private AppWindow? _appWindow;

        private bool _trayExitRequested;
        private bool _isHiddenToTray;

        private bool _closePromptInProgress;
        private bool _directCloseRequested;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;

            var hwnd = WindowNative.GetWindowHandle(this);
            _hwnd = hwnd;

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += AppWindow_Closing;

            // 标准高度标题栏：与内置 PaneToggle 尺寸一致；先设 PreferredHeightOption，再 SetTitleBar
            ApplyStandardTitleBarChrome();

            SetTitleBar(AppTitleBar);

            // When enabled, "closing" will hide window to system tray instead of exiting.
            this.Closed += MainWindow_Closed;

            // Hook WM_NCLBUTTONDBLCLK to prevent compact-mode caption double-click maximizing.
            // This avoids rapid clicking the title-bar adjacent button being interpreted as a caption double-click.
            _wndProcDelegate = WndProcHook;
            _oldWndProcPtr = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            // Initialize tray icon based on current settings.
            var config = ConfigHelper.LoadConfig();
            bool minimizeToTray = config.MinimizeToTrayOnClose;

            _trayIcon = new Win32TrayIcon(
                hwnd: hwnd,
                callbackMessage: TrayCallbackMessage,
                // 托盘图标始终创建并可见；关闭行为由 MinimizeToTrayOnClose 决定
                startHidden: false,
                tooltip: "CourseList",
                onRestoreRequested: RestoreFromTray,
                onOpenHomeRequested: OpenHomeFromTrayMenu,
                onExitRequested: ExitFromTray);

            UpdateTrayCloseBehavior(minimizeToTray);

        }

        private void ApplyStandardTitleBarChrome()
        {
            if (_appWindow == null)
                return;
            try
            {
                _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            }
            catch
            {
                // 忽略兼容性异常
            }
        }

        private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WM_GETMINMAXINFO)
                {
                    // 通过 Win32 获取最小拖拽尺寸，防止窗口缩到过窄后“回闪到最小宽度”。
                    // 该方式不做 Move/Resize，不干预后续放大/缩小手感。
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    // Win32 的 ptMinTrackSize 使用“像素”，而 WinUI 的宽度/阈值是 “view pixel(DIP)”。
                    // 在高 DPI 下 DIP->像素换算必须使用 Win32 DPI（不能用 DisplayInformation，因为 WndProc 非 CoreWindow 线程）。
                    uint dpi = GetDpiForWindow(hWnd);
                    if (dpi == 0) dpi = 96; // fallback
                    double pixelPerDip = dpi / 96.0;
                    mmi.ptMinTrackSize.X = (int)Math.Round(MinWindowWidth * pixelPerDip);
                    Marshal.StructureToPtr(mmi, lParam, true);
                }

                if (msg == WM_NCLBUTTONDBLCLK)
                {
                    // Block caption double-click maximize globally to avoid title-bar button click conflicts.
                    if (wParam.ToInt32() == HTCAPTION)
                        return IntPtr.Zero;
                }
            }
            catch
            {
                // ignore
            }

            return CallWindowProc(_oldWndProcPtr, hWnd, msg, wParam, lParam);
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

        private void OpenHomeFromTrayMenu()
        {
            if (_trayExitRequested)
                return;

            // 只“显示/置顶/激活”，不做页面导航。
            if (_isHiddenToTray)
            {
                RestoreFromTray();
            }
            else
            {
                // 如果只是被最小化到后台/遮挡，确保窗口显示出来。
                _appWindow?.Show();
                Activate();
                return;
            }

            Activate();
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
            _isHiddenToTray = true;
            if (_appWindow != null)
            {
                _appWindow.IsShownInSwitchers = false;
                _appWindow.Hide();
            }
            _trayIcon?.SetHidden(false);
        }

        private async Task ShowClosePromptAsync()
        {
            try
            {
                var config = ConfigHelper.LoadConfig();

                // Two radio options + "do not prompt again"
                var minimizeRadio = new RadioButton
                {
                    Content = "最小化到系统托盘",
                    IsChecked = config.MinimizeToTrayOnClose,
                    GroupName = "CloseBehavior"
                };

                var directRadio = new RadioButton
                {
                    Content = "直接关闭",
                    IsChecked = !config.MinimizeToTrayOnClose,
                    GroupName = "CloseBehavior"
                };

                var dontRemindCheck = new CheckBox
                {
                    Content = "不再提示",
                    IsChecked = false
                };

                var stack = new StackPanel { Spacing = 10 };
                stack.Children.Add(new TextBlock
                {
                    Text = "请选择关闭方式：",
                });
                stack.Children.Add(minimizeRadio);
                stack.Children.Add(directRadio);
                stack.Children.Add(dontRemindCheck);

                var dialog = new ContentDialog
                {
                    Title = "点击关闭按钮后：",
                    Content = stack,
                    PrimaryButtonText = "确定",
                    SecondaryButtonText = "取消",
                    XamlRoot = this.Content?.XamlRoot
                        ?? RootFrame?.XamlRoot
                };

                // Ensure dialog uses current theme.
                dialog.RequestedTheme = (RootFrame as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;

                var result = await ContentDialogGuard.ShowAsync(dialog);
                if (result != ContentDialogResult.Primary)
                    return;

                bool chooseTray = minimizeRadio.IsChecked == true;
                bool dontRemind = dontRemindCheck.IsChecked == true;

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

            _isHiddenToTray = false;

            if (_appWindow != null)
            {
                _appWindow.IsShownInSwitchers = true;
                _appWindow.Show();
            }

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

            if (_isHiddenToTray)
            {
                RestoreFromTray();
                return;
            }

            if (_appWindow != null)
            {
                _appWindow.IsShownInSwitchers = true;
                _appWindow.Show();
            }

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
            if (_isHiddenToTray)
            {
                _isHiddenToTray = false;
                if (_appWindow != null)
                {
                    _appWindow.IsShownInSwitchers = true;
                    _appWindow.Show();
                }
                Activate();
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            try
            {
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
