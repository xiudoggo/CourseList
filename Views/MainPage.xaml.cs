using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CourseList.Views
{
    public sealed partial class MainPage : Page
    {
        private bool _isCompactMode;

        public MainPage()
        {
            this.InitializeComponent();

            // 默认显示首页
            ContentFrame.Navigate(typeof(HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];

            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (!_isCompactMode)
                return;

            if (ContentFrame?.Content is SchedulePage sp) sp.ApplyCompactMode(true);
            if (ContentFrame?.Content is HomePage hp) hp.ApplyCompactMode(true);
            if (ContentFrame?.Content is CourseListPage clp) clp.ApplyCompactMode(true);
            if (ContentFrame?.Content is SettingsPage spg) spg.ApplyCompactMode(true);
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

            // 小窗模式下：点击菜单后收起侧边栏，避免遮挡内容。
            if (_isCompactMode)
                sender.IsPaneOpen = false;

            // 如果切到课程表页，确保同步小窗模式。
            if (ContentFrame?.Content is SchedulePage sp2)
                sp2.ApplyCompactMode(_isCompactMode);
            if (ContentFrame?.Content is HomePage hp2)
                hp2.ApplyCompactMode(_isCompactMode);
            if (ContentFrame?.Content is CourseListPage clp2)
                clp2.ApplyCompactMode(_isCompactMode);
            if (ContentFrame?.Content is SettingsPage spg2)
                spg2.ApplyCompactMode(_isCompactMode);
        }

        public void ApplyCompactMode(bool isCompact)
        {
            _isCompactMode = isCompact;

            if (isCompact)
            {
                // 覆盖式抽屉：进入小窗时默认必须隐藏，只有点击按钮才覆盖显示。
                NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                NavView.CompactPaneLength = 0; // 让紧凑长度不占用空间
                NavView.IsPaneOpen = false;
            }
            else
            {
                NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                NavView.CompactPaneLength = 48;
                NavView.IsPaneOpen = true;
            }

            // 透传给当前正在显示的课程表页（如果有）
            if (ContentFrame?.Content is HomePage hp)
                hp.ApplyCompactMode(isCompact);

            if (ContentFrame?.Content is SchedulePage sp)
                sp.ApplyCompactMode(isCompact);

            if (ContentFrame?.Content is CourseListPage clp)
                clp.ApplyCompactMode(isCompact);

            if (ContentFrame?.Content is SettingsPage spg)
                spg.ApplyCompactMode(isCompact);
        }

        public void TogglePaneFromTitleBar()
        {
            if (!_isCompactMode)
                return;

            NavView.IsPaneOpen = !NavView.IsPaneOpen;
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
            var frame = ContentFrame;
            if (frame != null && frame.CurrentSourcePageType != typeof(HomePage))
                frame.Navigate(typeof(HomePage));
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
