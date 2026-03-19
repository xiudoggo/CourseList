using Microsoft.UI.Xaml;


namespace CourseList.Helpers;


public static class ThemeHelper
{
    public static void ApplyTheme(string themeStr)
    {
        ElementTheme theme = themeStr switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }
}