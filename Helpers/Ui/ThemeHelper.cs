using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using CourseList;
using Windows.UI;
using Microsoft.UI;
using WinRT.Interop;


namespace CourseList.Helpers;


public static class ThemeHelper
{
    private static bool _subscribedToActualThemeChanged;
    private static readonly List<WeakReference<Window>> PinWindows = new();

    public static void RegisterPinWindow(Window window)
    {
        PinWindows.Add(new WeakReference<Window>(window));
        if (App.CurrentMainWindow?.Content is FrameworkElement mainFe && window.Content is FrameworkElement pinFe)
            pinFe.RequestedTheme = mainFe.RequestedTheme;
        SyncPinWindowChrome(window);
    }

    public static void UnregisterPinWindow(Window window)
    {
        for (int i = PinWindows.Count - 1; i >= 0; i--)
        {
            if (!PinWindows[i].TryGetTarget(out var w) || ReferenceEquals(w, window))
                PinWindows.RemoveAt(i);
        }
    }

    /// <summary>根据主窗口当前实际主题同步置顶窗标题栏按钮色。</summary>
    public static void SyncPinWindowChrome(Window window)
    {
        if (App.CurrentMainWindow?.Content is FrameworkElement mainRoot)
        {
            UpdateTitleBarForWindow(window, mainRoot.ActualTheme == ElementTheme.Dark);
            return;
        }

        if (window.Content is FrameworkElement fe)
            UpdateTitleBarForWindow(window, fe.ActualTheme == ElementTheme.Dark);
    }

    public static void ApplyTheme(string? themeStr)
    {
        ElementTheme theme = themeStr switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        FrameworkElement? root = App.CurrentMainWindow?.Content as FrameworkElement;
        if (root != null)
        {
            root.RequestedTheme = theme;

            // 跟随系统：RequestedTheme 仍是 Default，必须用 ActualTheme 判断真正的深浅。
            if (!_subscribedToActualThemeChanged)
            {
                _subscribedToActualThemeChanged = true;
                root.ActualThemeChanged += (_, __) =>
                {
                    bool actualIsDark = root.ActualTheme == ElementTheme.Dark;
                    UpdateTitleBarButtons(actualIsDark);
                    SyncPinnedWindowsTitleBarsFromMain(root);
                };
            }

            bool effectiveIsDark = (theme == ElementTheme.Default ? root.ActualTheme : theme) == ElementTheme.Dark;
            UpdateTitleBarButtons(effectiveIsDark);
            ApplyThemeToPinWindows(theme, root);
            return;
        }

        // fallback：拿不到 root 时至少按 Requested 的 themeStr 设置。
        UpdateTitleBarButtons(theme == ElementTheme.Dark);
    }

    private static void ApplyThemeToPinWindows(ElementTheme theme, FrameworkElement mainRoot)
    {
        bool titleBarDark = (theme == ElementTheme.Default ? mainRoot.ActualTheme : theme) == ElementTheme.Dark;
        foreach (var wr in PinWindows)
        {
            if (!wr.TryGetTarget(out var win) || win.Content is not FrameworkElement fe)
                continue;
            fe.RequestedTheme = theme;
            UpdateTitleBarForWindow(win, titleBarDark);
        }
    }

    private static void SyncPinnedWindowsTitleBarsFromMain(FrameworkElement mainRoot)
    {
        bool isDark = mainRoot.ActualTheme == ElementTheme.Dark;
        foreach (var wr in PinWindows)
        {
            if (wr.TryGetTarget(out var win))
                UpdateTitleBarForWindow(win, isDark);
        }
    }

    private static void UpdateTitleBarButtons(bool isDark)
    {
        try
        {
            var mainWindow = App.CurrentMainWindow;
            if (mainWindow == null)
                return;

            UpdateTitleBarForWindow(mainWindow, isDark);
        }
        catch
        {
            // 忽略标题栏 API 异常，避免影响主流程
        }
    }

    private static void UpdateTitleBarForWindow(Window window, bool isDark)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow?.TitleBar != null)
            {
                appWindow.TitleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
                appWindow.TitleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;

                var hoverBg = isDark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0);
                var pressedBg = isDark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(90, 0, 0, 0);
                var inactiveBg = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

                appWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
                appWindow.TitleBar.ButtonPressedBackgroundColor = pressedBg;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = inactiveBg;
            }
        }
        catch
        {
            // 忽略标题栏 API 异常
        }
    }
}
