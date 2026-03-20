using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System;
using CourseList;
using Windows.UI;
using Microsoft.UI;
using WinRT.Interop;


namespace CourseList.Helpers;


public static class ThemeHelper
{
    private static bool _subscribedToActualThemeChanged;

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
                };
            }

            bool effectiveIsDark = (theme == ElementTheme.Default ? root.ActualTheme : theme) == ElementTheme.Dark;
            UpdateTitleBarButtons(effectiveIsDark);
            return;
        }

        // fallback：拿不到 root 时至少按 Requested 的 themeStr 设置。
        UpdateTitleBarButtons(theme == ElementTheme.Dark);
    }

    private static void UpdateTitleBarButtons(bool isDark)
    {
        try
        {
            var mainWindow = App.CurrentMainWindow;
            if (mainWindow == null)
                return;

            var hwnd = WindowNative.GetWindowHandle(mainWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow?.TitleBar != null)
            {
                // 设置前景色
                appWindow.TitleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
                appWindow.TitleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;

                // 根据主题设置 hover/pressed 背景色，避免始终为黑色
                // 这几个属性是可选自定义；如果不支持，外层 try/catch 会忽略。
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
            // 忽略标题栏 API 异常，避免影响主流程
        }
    }
}