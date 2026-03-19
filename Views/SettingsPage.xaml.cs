using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using CourseList.Helpers;

using System.Threading.Tasks;

using Microsoft.UI.Xaml.Input;

namespace CourseList.Views
{
    public sealed partial class SettingsPage : Page
    {
        private AppConfig? _config;
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            _config = ConfigHelper.LoadConfig();

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                var tag = item.Tag as string;
                if (!string.IsNullOrEmpty(tag) && tag == _config.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }

            // 课程表显示范围（周一到周五 / 周一到周日）
            foreach (ComboBoxItem item in WeekRangeComboBox.Items)
            {
                var tag = item.Tag as string;
                if (int.TryParse(tag, out var range) && range == _config.ScheduleWeekRange)
                {
                    WeekRangeComboBox.SelectedItem = item;
                    break;
                }
            }

            _isInitializing = false;
        }

    

        private void ThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _config == null)
                return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem item)
            {
                string theme = item.Tag as string ?? "Default";

                if (string.Equals(_config.Theme, theme, System.StringComparison.OrdinalIgnoreCase))
                    return;

                // 应用主题
                ThemeHelper.ApplyTheme(theme);

                // 保存配置
                _config.Theme = theme;
                _ = Task.Run(() => ConfigHelper.SaveConfig(_config));
            }
        }

        private void WeekRangeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _config == null)
                return;

            if (WeekRangeComboBox.SelectedItem is not ComboBoxItem item)
                return;

            var tag = item.Tag as string;
            if (!int.TryParse(tag, out var range))
                return;

            // 只允许 5 或 7
            range = range == 5 ? 5 : 7;

            if (_config.ScheduleWeekRange == range)
                return;

            _config.ScheduleWeekRange = range;
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));
        }
    }
}