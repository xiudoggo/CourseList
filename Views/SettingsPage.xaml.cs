using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
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
        private TodoPinWindow? _sizingPreviewWindow;

        private Thickness _desktopOuterMargin = new Thickness(20);
        private Thickness _desktopOuterPadding = new Thickness(20);
        private GridLength _desktopLeftColumnWidth = GridLength.Auto;
        private GridLength _desktopMiddleColumnWidth = GridLength.Auto;
        private GridLength _desktopRightColumnWidth = GridLength.Auto;
        private GridLength _desktopSpacerColumnWidth = new GridLength(1, GridUnitType.Star);

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

            RefreshSchemeComboBox();
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

        public void ApplyCompactMode(bool isCompact)
        {
            if (SettingsOuterBorder == null || SettingsMainGrid == null || SettingsLeftPanel == null || SettingsRightPanel == null || SettingsThirdPanel == null)
                return;

            if (isCompact)
            {
                SettingsOuterBorder.Margin = new Thickness(12);
                SettingsOuterBorder.Padding = new Thickness(12);
                SettingsRightPanel.Margin = new Thickness(0);
                SettingsThirdPanel.Margin = new Thickness(0);

                // 竖屏 compact：左/右两段竖直排列（系统设置在上，课程表功能在下）
                SettingsLeftPanel.Visibility = Visibility.Visible;
                SettingsThirdPanel.Visibility = Visibility.Visible;

                if (SettingsMainGrid.ColumnDefinitions.Count >= 4)
                {
                    SettingsMainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    SettingsMainGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    SettingsMainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    SettingsMainGrid.ColumnDefinitions[3].Width = new GridLength(0);
                }

                Grid.SetColumn(SettingsLeftPanel, 0);
                Grid.SetRow(SettingsLeftPanel, 0);

                Grid.SetColumn(SettingsRightPanel, 0);
                Grid.SetRow(SettingsRightPanel, 1);

                Grid.SetColumn(SettingsThirdPanel, 0);
                Grid.SetRow(SettingsThirdPanel, 2);
            }
            else
            {
                SettingsOuterBorder.Margin = _desktopOuterMargin;
                SettingsOuterBorder.Padding = _desktopOuterPadding;
                SettingsRightPanel.Margin = new Thickness(22, 0, 0, 0);
                SettingsThirdPanel.Margin = new Thickness(22, 0, 0, 0);

                SettingsLeftPanel.Visibility = Visibility.Visible;
                SettingsThirdPanel.Visibility = Visibility.Visible;
                if (SettingsMainGrid.ColumnDefinitions.Count >= 1)
                    SettingsMainGrid.ColumnDefinitions[0].Width = _desktopLeftColumnWidth;

                if (SettingsMainGrid.ColumnDefinitions.Count >= 2)
                    SettingsMainGrid.ColumnDefinitions[1].Width = _desktopMiddleColumnWidth;
                if (SettingsMainGrid.ColumnDefinitions.Count >= 3)
                    SettingsMainGrid.ColumnDefinitions[2].Width = _desktopRightColumnWidth;
                if (SettingsMainGrid.ColumnDefinitions.Count >= 4)
                    SettingsMainGrid.ColumnDefinitions[3].Width = _desktopSpacerColumnWidth;

                Grid.SetColumn(SettingsLeftPanel, 0);
                Grid.SetRow(SettingsLeftPanel, 0);

                Grid.SetColumn(SettingsRightPanel, 1);
                Grid.SetRow(SettingsRightPanel, 0);

                Grid.SetColumn(SettingsThirdPanel, 2);
                Grid.SetRow(SettingsThirdPanel, 0);
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

                RefreshSchemeComboBox();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void RefreshSchemeComboBox()
        {
            var (schemes, currentId) = SchemeHelper.LoadSchemes();
            SchemeComboBox.ItemsSource = schemes;
            SchemeComboBox.SelectedItem = schemes.FirstOrDefault(s => s.Id == currentId);
            UpdateSchemeButtonsState();
        }

        private void UpdateSchemeButtonsState()
        {
            var (schemes, currentId) = SchemeHelper.LoadSchemes();
            var selected = SchemeComboBox.SelectedItem as SchemeInfo;
            RenameSchemeBtn.IsEnabled = selected != null && selected.Id == currentId;
            DeleteSchemeBtn.IsEnabled = schemes.Count > 1;
        }

        private void SchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || SchemeComboBox.SelectedItem is not SchemeInfo si)
                return;

            var currentId = SchemeHelper.GetCurrentSchemeId();
            if (si.Id == currentId)
            {
                UpdateSchemeButtonsState();
                return;
            }

            SchemeHelper.SetCurrentSchemeId(si.Id);
            _config = ConfigHelper.LoadConfig();
            _isInitializing = true;
            foreach (ComboBoxItem item in WeekRangeComboBox.Items)
            {
                var tag = item.Tag as string;
                if (int.TryParse(tag, out var range) && range == _config.ScheduleWeekRange)
                {
                    WeekRangeComboBox.SelectedItem = item;
                    break;
                }
            }
            EnsurePeriodTimeRangesCapacity(20);
            PeriodCountBox.Value = _config.PeriodCount;
            SemesterStartDatePicker.Date = new DateTimeOffset(_config.SemesterStartMonday.Date);
            SemesterTotalWeeksBox.Value = _config.SemesterTotalWeeks;
            RebuildPeriodTimeInputs();
            _periodTimeDirty = false;
            ConfirmPeriodChangesButton.IsEnabled = false;
            _isInitializing = false;
            UpdateSchemeButtonsState();
            SystemNotificationHelper.ShowSilent("课表方案", $"已切换到「{si.Name}」");
        }

        private async void AddSchemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox
            {
                Header = "方案名称",
                PlaceholderText = "输入方案名称",
                Width = 280
            };
            var copyCheck = new CheckBox
            {
                Content = "复制当前方案的课程和设置",
                IsChecked = true
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(nameBox);
            panel.Children.Add(copyCheck);

            var dialog = new ContentDialog
            {
                Title = "新建方案",
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            if (result != ContentDialogResult.Primary)
                return;

            var name = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowToast("请输入方案名称");
                return;
            }

            var currentId = SchemeHelper.GetCurrentSchemeId();
            var copyFromId = copyCheck.IsChecked == true ? currentId : null;
            var newId = SchemeHelper.CreateScheme(name, copyFromId);
            if (newId != null)
            {
                SchemeHelper.SetCurrentSchemeId(newId);
                _config = ConfigHelper.LoadConfig();
                _isInitializing = true;
                foreach (ComboBoxItem item in WeekRangeComboBox.Items)
                {
                    var tag = item.Tag as string;
                    if (int.TryParse(tag, out var range) && range == _config.ScheduleWeekRange)
                    {
                        WeekRangeComboBox.SelectedItem = item;
                        break;
                    }
                }
                EnsurePeriodTimeRangesCapacity(20);
                PeriodCountBox.Value = _config.PeriodCount;
                SemesterStartDatePicker.Date = new DateTimeOffset(_config.SemesterStartMonday.Date);
                SemesterTotalWeeksBox.Value = _config.SemesterTotalWeeks;
                RebuildPeriodTimeInputs();
                _periodTimeDirty = false;
                ConfirmPeriodChangesButton.IsEnabled = false;
                _isInitializing = false;
                RefreshSchemeComboBox();
                SystemNotificationHelper.ShowSilent("课表方案", $"已创建「{name}」");
            }
        }

        private async void RenameSchemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var currentId = SchemeHelper.GetCurrentSchemeId();
            var (schemes, _) = SchemeHelper.LoadSchemes();
            var current = schemes.FirstOrDefault(s => s.Id == currentId);
            if (current == null)
                return;

            var nameBox = new TextBox
            {
                Header = "新名称",
                Text = current.Name,
                Width = 280
            };
            var dialog = new ContentDialog
            {
                Title = "重命名方案",
                Content = nameBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            if (result != ContentDialogResult.Primary)
                return;

            var newName = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                ShowToast("请输入新名称");
                return;
            }

            if (SchemeHelper.RenameScheme(currentId, newName))
            {
                RefreshSchemeComboBox();
                ShowToast("已重命名");
            }
        }

        private async void DeleteSchemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var currentId = SchemeHelper.GetCurrentSchemeId();
            var (schemes, _) = SchemeHelper.LoadSchemes();
            if (schemes.Count <= 1)
            {
                ShowToast("至少需保留一个方案");
                return;
            }

            var listPanel = new StackPanel { Spacing = 8 };
            var radioButtons = new List<RadioButton>();
            foreach (var scheme in schemes)
            {
                bool isCurrent = scheme.Id == currentId;
                var rb = new RadioButton
                {
                    Content = isCurrent ? $"{scheme.Name}（当前方案无法删除）" : scheme.Name,
                    Tag = scheme.Id,
                    IsEnabled = !isCurrent
                };
                radioButtons.Add(rb);
                listPanel.Children.Add(rb);
            }

            var pickerDialog = new ContentDialog
            {
                Title = "选择要删除的方案",
                Content = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = listPanel
                },
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            var result = await ContentDialogGuard.ShowAsync(pickerDialog);
            if (result != ContentDialogResult.Primary)
                return;

            var target = radioButtons.FirstOrDefault(r => r.IsChecked == true);
            if (target?.Tag is not string targetId || string.IsNullOrEmpty(targetId))
            {
                ShowToast("请先选择一个方案");
                return;
            }

            var targetScheme = schemes.FirstOrDefault(s => s.Id == targetId);
            if (targetScheme == null)
                return;

            var confirmDialog = new ContentDialog
            {
                Title = "确认删除？",
                Content = $"确定要删除方案「{targetScheme.Name}」吗？该方案下的课程和设置将被移除，且无法恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            var confirmResult = await ContentDialogGuard.ShowAsync(confirmDialog);
            if (confirmResult != ContentDialogResult.Primary)
                return;

            if (SchemeHelper.DeleteScheme(targetId))
            {
                RefreshSchemeComboBox();
                SystemNotificationHelper.ShowSilent("课表方案", $"已删除「{targetScheme.Name}」");
            }
        }

        private void OpenImportButton_Click(object sender, RoutedEventArgs e)
        {
            ImportWebViewWindow.OpenOrActivate();
        }

        private void ShowToast(string message)
        {
            var toast = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };
            _ = ContentDialogGuard.ShowAsync(toast);
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

        private void AdjustTodoPinWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sizingPreviewWindow is { IsDisposed: false })
            {
                _sizingPreviewWindow.EnsureActivated();
                return;
            }

            AdjustTodoPinWindowSizeButton.IsEnabled = false;
            _sizingPreviewWindow = new TodoPinWindow(
                autoActivate: true,
                isSizingMode: true,
                onSizingConfirmed: (widthDip, heightDip) =>
                {
                    var cfg = _config ?? ConfigHelper.LoadConfig();
                    cfg.TodoPinWindowWidthDip = widthDip;
                    cfg.TodoPinWindowHeightDip = heightDip;
                    _config = cfg;
                    _ = Task.Run(() => ConfigHelper.SaveConfig(cfg));
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowToast($"置顶待办窗口尺寸已保存: {Math.Round(cfg.TodoPinWindowWidthDip)} x {Math.Round(cfg.TodoPinWindowHeightDip)} DIP");
                    });
                });

            _sizingPreviewWindow.Closed += (_, _) =>
            {
                _sizingPreviewWindow = null;
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    AdjustTodoPinWindowSizeButton.IsEnabled = true;
                });
            };
            _sizingPreviewWindow.EnsureActivated();
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