using CourseList;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CourseList.Helpers;

/// <summary>
/// 主题资源查找（如选中态描边）；取消选中时的卡片填充使用实色。
/// 有效主题以 XamlRoot / 主窗口为准：ItemsRepeater 内 Border 的 ActualTheme 可能不可靠。
/// </summary>
public static class ThemeResourceHelper
{
    /// <summary>与 <see cref="ResolveEffectiveTheme"/> 一致的 ThemeDictionaries 键（Light / Dark）。</summary>
    public static string ThemeKeyForElement(FrameworkElement fe) =>
        ResolveEffectiveTheme(fe) == ElementTheme.Dark ? "Dark" : "Light";

    /// <summary>解析 App.xaml 中 CourseListCard* 笔刷（仅从应用 ThemeDictionaries 读取，避免与合并的 Fluent 字典混淆）。</summary>
    public static bool TryGetThemeBrush(FrameworkElement context, string resourceKey, out Brush? brush)
    {
        brush = null;
        if (Application.Current?.Resources is not { } root)
            return false;
        var themeKey = ThemeKeyForElement(context);
        if (root.ThemeDictionaries is not { Count: > 0 } themes)
            return false;
        if (!themes.TryGetValue(themeKey, out var themedObj) || themedObj is not ResourceDictionary themed)
            return false;
        if (!themed.TryGetValue(resourceKey, out var obj) || obj is not Brush b)
            return false;
        brush = b;
        return true;
    }

    /// <summary>取消选中：与 App.xaml 中 CourseListCard* 实色笔刷一致（浅色纯白）。</summary>
    public static void ApplyDefaultCardChrome(FrameworkElement context, Border border)
    {
        if (TryGetThemeBrush(context, "CourseListCardBackgroundBrush", out var bg) && bg != null)
            border.Background = bg;
        else
        {
            bool isDark = ResolveEffectiveTheme(context) == ElementTheme.Dark;
            border.Background = new SolidColorBrush(isDark ? Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
        }

        if (TryGetThemeBrush(context, "CourseListCardStrokeBrush", out var stroke) && stroke != null)
            border.BorderBrush = stroke;
        else
        {
            bool isDark = ResolveEffectiveTheme(context) == ElementTheme.Dark;
            border.BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(255, 0x48, 0x48, 0x48) : Color.FromArgb(255, 0xD0, 0xD0, 0xD0));
        }
    }

    /// <summary>与页面 / 窗口及 {ThemeResource} 解析一致的有效主题（不直接使用模板内子元素的 ActualTheme）。</summary>
    public static ElementTheme ResolveEffectiveTheme(FrameworkElement fe)
    {
        if (fe.XamlRoot?.Content is FrameworkElement xr)
            return xr.ActualTheme;

        if (App.CurrentMainWindow?.Content is FrameworkElement winContent)
            return winContent.ActualTheme;

        for (DependencyObject? o = fe; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (o is Page p)
                return p.ActualTheme;
        }

        return fe.ActualTheme;
    }
}
