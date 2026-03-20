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

        /// <summary>
        /// 从托盘/外部入口切回“首页”。
        /// </summary>
        public void ShowHomePage()
        {
            // NavigationView 的第一个 MenuItem 在 XAML 中对应 HomePage。
            if (NavView?.MenuItems?.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
                return;
            }

            // 兜底：如果 MenuItems 为空，直接导航
            if (ContentFrame?.CurrentSourcePageType != typeof(HomePage))
                ContentFrame.Navigate(typeof(HomePage)); 
        }

        /// <summary>
        /// 仅当当前显示的是 SettingsPage 时，刷新“关闭相关设置”的 UI 状态。
        /// 用于从系统托盘恢复窗口时，避免设置页因页面复用显示旧状态。
        /// </summary>
        public void RefreshCloseOptionsIfSettingsPageVisible()
        {
            if (ContentFrame?.Content is SettingsPage sp)
            {
                sp.RefreshCloseOptionsFromConfig();
            }
        }
    }
}
