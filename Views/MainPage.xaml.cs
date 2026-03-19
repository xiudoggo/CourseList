using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CourseList.Views
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            // 默认显示首页
            ContentFrame.Navigate(typeof(HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
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
                    ContentFrame.Navigate(pageType);
            }
        }
    }
}
