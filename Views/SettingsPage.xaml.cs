using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using CourseList.Helpers;

using System.Threading.Tasks;

using Microsoft.UI.Xaml.Input;
using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CourseList.Views
{
    public sealed partial class SettingsPage : Page
    {
        private sealed class PeriodTimeButtonTag
        {
            public int Index { get; set; }
            public bool IsStart { get; set; }
        }

        private AppConfig? _config;
        private bool _isInitializing = true;
        private bool _periodTimeDirty = false;

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

            // 关闭行为：最小化到托盘 / 直接关闭
            MinimizeToTrayRadio.IsChecked = _config.MinimizeToTrayOnClose;
            DirectCloseRadio.IsChecked = !_config.MinimizeToTrayOnClose;

            // 关闭时提示
            ClosePromptCheckBox.IsChecked = _config.ClosePromptEnabled;

            // 每日节数 + 每节时间
            EnsurePeriodTimeRangesCapacity(20);
            PeriodCountBox.Value = _config.PeriodCount;
            SemesterStartDatePicker.Date = new DateTimeOffset(_config.SemesterStartMonday.Date);
            SemesterTotalWeeksBox.Value = _config.SemesterTotalWeeks;
            RebuildPeriodTimeInputs();

            _isInitializing = false;
        }

        /// <summary>
        /// 从 config.json 刷新“关闭行为/关闭时提示”等关闭相关 UI。
        /// 用于窗口从系统托盘恢复时，避免页面不触发 OnNavigatedTo 导致显示过期。
        /// </summary>
        public void RefreshCloseOptionsFromConfig()
        {
            try
            {
                _isInitializing = true;

                var cfg = ConfigHelper.LoadConfig();
                if (_config == null)
                    _config = cfg;
                else
                {
                    _config.MinimizeToTrayOnClose = cfg.MinimizeToTrayOnClose;
                    _config.ClosePromptEnabled = cfg.ClosePromptEnabled;
                }

                MinimizeToTrayRadio.IsChecked = _config.MinimizeToTrayOnClose;
                DirectCloseRadio.IsChecked = !_config.MinimizeToTrayOnClose;
                ClosePromptCheckBox.IsChecked = _config.ClosePromptEnabled;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 每次进入设置页都重新读取 config.json，避免页面复用导致 UI 显示旧状态。
            try
            {
                _isInitializing = true;
                _periodTimeDirty = false;
                ConfirmPeriodChangesButton.IsEnabled = false;

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

                foreach (ComboBoxItem item in WeekRangeComboBox.Items)
                {
                    var tag = item.Tag as string;
                    if (int.TryParse(tag, out var range) && range == _config.ScheduleWeekRange)
                    {
                        WeekRangeComboBox.SelectedItem = item;
                        break;
                    }
                }

                MinimizeToTrayRadio.IsChecked = _config.MinimizeToTrayOnClose;
                DirectCloseRadio.IsChecked = !_config.MinimizeToTrayOnClose;
                ClosePromptCheckBox.IsChecked = _config.ClosePromptEnabled;

                EnsurePeriodTimeRangesCapacity(20);
                PeriodCountBox.Value = _config.PeriodCount;
                SemesterStartDatePicker.Date = new DateTimeOffset(_config.SemesterStartMonday.Date);
                SemesterTotalWeeksBox.Value = _config.SemesterTotalWeeks;
                RebuildPeriodTimeInputs();
            }
            finally
            {
                _isInitializing = false;
            }
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

        private void SemesterStartDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs args)
        {
            if (_isInitializing || _config == null)
                return;

            if (sender is not DatePicker picker)
                return;

            var selectedDate = picker.Date.Date;
            int diff = ((int)selectedDate.DayOfWeek + 6) % 7; // Monday=0
            var monday = selectedDate.AddDays(-diff).Date;

            if (_config.SemesterStartMonday.Date == monday)
                return;

            _config.SemesterStartMonday = monday;
            picker.Date = new DateTimeOffset(monday);
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));
        }

        private void SemesterTotalWeeksBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing || _config == null)
                return;

            int weeks = (int)Math.Round(sender.Value);
            weeks = Math.Clamp(weeks, 1, 30);

            if (_config.SemesterTotalWeeks == weeks)
                return;

            _config.SemesterTotalWeeks = weeks;
            sender.Value = weeks;
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));
        }

        private void PeriodCountBox_ValueChanged(NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing || _config == null)
                return;

            int newCount = (int)Math.Round(sender.Value);
            newCount = newCount < 1 ? 1 : newCount > 20 ? 20 : newCount;

            if (_config.PeriodCount == newCount)
                return;

            _config.PeriodCount = newCount;
            // PeriodTimeRanges 永远保留 20 行，不因为缩小节数而删除数据
            EnsurePeriodTimeRangesCapacity(20);

            RebuildPeriodTimeInputs();

            MarkPeriodTimeDirty();
        }

        private void EnsurePeriodTimeRangesCapacity(int maxPeriods)
        {
            if (_config == null)
                return;

            _config.PeriodTimeRanges ??= new System.Collections.Generic.List<PeriodTimeRange>();

            if (_config.PeriodTimeRanges.Count < maxPeriods)
            {
                while (_config.PeriodTimeRanges.Count < maxPeriods)
                    _config.PeriodTimeRanges.Add(new PeriodTimeRange());
            }
            else if (_config.PeriodTimeRanges.Count > maxPeriods)
            {
                _config.PeriodTimeRanges.RemoveRange(maxPeriods, _config.PeriodTimeRanges.Count - maxPeriods);
            }
        }

        private void RebuildPeriodTimeInputs()
        {
            if (_config == null)
                return;

            PeriodTimeInputsPanel.Children.Clear();

            for (int i = 0; i < _config.PeriodCount; i++)
            {
                int index = i;

                var rowGrid = new Grid
                {
                    Margin = new Thickness(0, 6, 0, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(44) },
                        new ColumnDefinition { Width = new GridLength(90) },
                        new ColumnDefinition { Width = new GridLength(12) },
                        new ColumnDefinition { Width = new GridLength(90) }
                    }
                };

                var label = new TextBlock
                {
                    Text = $"{index + 1}",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = 16
                };

                var range = _config.PeriodTimeRanges.Count > index
                    ? (_config.PeriodTimeRanges[index] ?? new PeriodTimeRange())
                    : new PeriodTimeRange();

                var startButton = new Button
                {
                    Tag = new PeriodTimeButtonTag { Index = index, IsStart = true },
                    Width = 90,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Content = FormatTime(range.StartTime ?? new TimeOnly(0, 0))
                };
                startButton.Click += PeriodTimeButton_Click;

                var separator = new TextBlock
                {
                    Text = "~",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                };

                var endButton = new Button
                {
                    Tag = new PeriodTimeButtonTag { Index = index, IsStart = false },
                    Width = 90,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Content = FormatTime(range.EndTime ?? range.StartTime ?? new TimeOnly(0, 0))
                };
                endButton.Click += PeriodTimeButton_Click;

                Grid.SetColumn(label, 0);
                Grid.SetColumn(startButton, 1);
                Grid.SetColumn(separator, 2);
                Grid.SetColumn(endButton, 3);

                rowGrid.Children.Add(label);
                rowGrid.Children.Add(startButton);
                rowGrid.Children.Add(separator);
                rowGrid.Children.Add(endButton);

                PeriodTimeInputsPanel.Children.Add(rowGrid);
            }
        }

        private static string FormatTime(TimeOnly time)
        {
            return $"{time:HH\\:mm}";
        }

        private void PeriodTimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_config == null || sender is not Button button || button.Tag is not PeriodTimeButtonTag tag)
                return;

            if (tag.Index < 0 || tag.Index >= _config.PeriodCount)
                return;

            var range = _config.PeriodTimeRanges[tag.Index] ?? new PeriodTimeRange();
            var current = tag.IsStart
                ? (range.StartTime ?? new TimeOnly(0, 0))
                : (range.EndTime ?? range.StartTime ?? new TimeOnly(0, 0));

            var picker = new TimePicker
            {
                ClockIdentifier = "24HourClock",
                MinuteIncrement = 5,
                Time = new TimeSpan(current.Hour, current.Minute, 0)
            };

            var okButton = new Button
            {
                Content = "确定",
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(picker);
            panel.Children.Add(okButton);

            var flyout = new Flyout
            {
                Content = panel,
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
            };

            okButton.Click += (_, __) =>
            {
                var selected = TimeOnly.FromTimeSpan(picker.Time);
                var changed = false;

                if (tag.IsStart)
                {
                    if (range.StartTime != selected)
                    {
                        range.StartTime = selected;
                        changed = true;
                    }
                    if (!range.EndTime.HasValue || range.EndTime.Value < range.StartTime.Value)
                    {
                        range.EndTime = range.StartTime;
                        changed = true;
                    }
                }
                else
                {
                    if (range.EndTime != selected)
                    {
                        range.EndTime = selected;
                        changed = true;
                    }
                    if (!range.StartTime.HasValue)
                    {
                        range.StartTime = range.EndTime;
                        changed = true;
                    }
                    else if (range.EndTime.Value < range.StartTime.Value)
                    {
                        range.StartTime = range.EndTime;
                        changed = true;
                    }
                }

                if (changed)
                {
                    _config.PeriodTimeRanges[tag.Index] = range;
                    RebuildPeriodTimeInputs();
                    MarkPeriodTimeDirty();
                }

                flyout.Hide();
            };

            button.Flyout = flyout;
            flyout.ShowAt(button);
        }

        private void MarkPeriodTimeDirty()
        {
            _periodTimeDirty = true;
            ConfirmPeriodChangesButton.IsEnabled = true;
        }

        private void ConfirmPeriodChanges_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_config == null)
                return;

            if (!_periodTimeDirty)
                return;

            // 统一确认：只在点按钮时写入 config.json
            _ = Task.Run(() =>
            {
                ConfigHelper.SaveConfig(_config);
            });

            _periodTimeDirty = false;
            ConfirmPeriodChangesButton.IsEnabled = false;
        }

        private void MinimizeToTrayRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null)
                return;

            if (_config.MinimizeToTrayOnClose)
                return;

            _config.MinimizeToTrayOnClose = true;
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));

            // 让托盘行为立即生效（无需重启）。
            if (CourseList.App.CurrentMainWindow is CourseList.MainWindow mw)
            {
                mw.UpdateTrayCloseBehavior(true);
            }
        }

        private void DirectCloseRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null)
                return;

            if (!_config.MinimizeToTrayOnClose)
                return;

            _config.MinimizeToTrayOnClose = false;
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));

            if (CourseList.App.CurrentMainWindow is CourseList.MainWindow mw)
            {
                mw.UpdateTrayCloseBehavior(false);
            }
        }

        private void ClosePromptCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null)
                return;

            bool newValue = ClosePromptCheckBox.IsChecked == true;
            if (_config.ClosePromptEnabled == newValue)
                return;

            _config.ClosePromptEnabled = newValue;
            _ = Task.Run(() => ConfigHelper.SaveConfig(_config));
        }
    }
}