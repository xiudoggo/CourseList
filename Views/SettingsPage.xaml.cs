using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using CourseList.Helpers;

using Microsoft.UI.Xaml.Input;

namespace CourseList.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            var config = ConfigHelper.LoadConfig();

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag.ToString() == config.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

    

        private void ThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item)
            {
                string theme = item.Tag.ToString();

                // 应用主题
                ThemeHelper.ApplyTheme(theme);

                // 保存配置
                var config = ConfigHelper.LoadConfig();
                config.Theme = theme;
                ConfigHelper.SaveConfig(config);
            }
        }
    }
}