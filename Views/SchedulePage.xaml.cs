using CourseList.Models;
using CourseList.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Linq;

namespace CourseList.Views
{
    public sealed partial class SchedulePage : Page
    {
        private List<Course> _courses = new List<Course>();
        private Dictionary<(int day, int period), Border> _cellMap = new();
        private bool _isScheduleGridInitialized = false;
        // 5=周一到周五；7=周一到周日
        private int _scheduleWeekRange = 7;
        private int _periodCount = 11;
        private List<string> _periodTimeRanges = new List<string>();

        public SchedulePage()
        {
            this.InitializeComponent();
            Loaded += SchedulePage_Loaded;
            
            // 初始化时隐藏操作面板
            ExpandedPanel.Visibility = Visibility.Collapsed;
        }

        private async void SchedulePage_Loaded(object sender, RoutedEventArgs e)
        {
            var config = ConfigHelper.LoadConfig();
            _scheduleWeekRange = config.ScheduleWeekRange == 5 ? 5 : 7;
            _periodCount = config.PeriodCount;
            _periodTimeRanges = config.PeriodTimeRanges ?? new List<string>();

            // 先根据配置重建列（周六/周日列删除或保留），再生成课程单元格
            ApplyWeekRangeVisibility();

            await LoadCoursesAsync();
            RebuildScheduleLayout();
            BuildScheduleGrid();
        }

        private async Task LoadCoursesAsync()
        {
            _courses = await CourseDataHelper.LoadCoursesAsync();
        }

        private void BuildScheduleGrid()
        {
            EnsureScheduleGridInitialized();
            ClearAllCourseCells();

            // 填充课程（只更新内容，不重建单元格）
            foreach (var course in _courses)
            {
                ApplyCourseToCells(course);
            }
        }

        private void ApplyWeekRangeVisibility()
        {
            // 5天模式：真正删除 Grid 的周六/周日列，让周一到周五的 '*' 列自动铺满。
            // 同时隐藏 XAML 里的周六/周日表头（row=0, col>=6）。
            bool showWeekend = _scheduleWeekRange == 7;

            // 0: 节次/星期（固定宽度），1..5: 周一到周五，6..7: 周六到周日
            int dayColumnCount = showWeekend ? 7 : 5; // 仅指 1..N 这部分
            int totalColumnCount = 1 + dayColumnCount; // + col=0

            // 重建 ColumnDefinitions（避免残留空白列）
            ScheduleGrid.ColumnDefinitions.Clear();

            // col=0
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            // col=1..N
            for (int col = 1; col <= dayColumnCount; col++)
            {
                ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // 隐藏 row=0 且落在被删除列上的表头元素
            if (!showWeekend)
            {
                foreach (var child in ScheduleGrid.Children)
                {
                    if (child is not FrameworkElement fe)
                        continue;

                    int row = Grid.GetRow(fe);
                    int col = Grid.GetColumn(fe);
                    if (row == 0 && col >= 6)
                        fe.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RebuildScheduleLayout()
        {
            // 删除旧的动态内容：课程格 + 旧的“节次/时间”首列
            var toRemove = new List<UIElement>();
            foreach (var child in ScheduleGrid.Children)
            {
                if (child is not FrameworkElement fe)
                    continue;

                // row=0 是星期标题行，其它全部清掉再重建
                if (Grid.GetRow(fe) >= 1)
                    toRemove.Add(child);
            }

            foreach (var child in toRemove)
                ScheduleGrid.Children.Remove(child);

            ScheduleGrid.RowDefinitions.Clear();

            // header row
            ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            // period rows
            for (int row = 1; row <= _periodCount; row++)
                ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });

            _cellMap.Clear();
            _courseCellMap.Clear();
            _isScheduleGridInitialized = false;

            BuildPeriodHeaderCells();
        }

        private void BuildPeriodHeaderCells()
        {
            var headerBg = this.Resources["ScheduleHeaderBrush"] as Brush;
            var headerTextBrush = this.Resources["ScheduleHeaderTextBrush"] as Brush;
            var cellStyle = this.Resources["CourseCellStyle"] as Style;

            for (int period = 1; period <= _periodCount; period++)
            {
                string timeText = _periodTimeRanges.Count >= period
                    ? _periodTimeRanges[period - 1]
                    : string.Empty;

                var border = new Border
                {
                    Style = cellStyle,
                    Background = headerBg,
                    BorderBrush = null,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0)
                };
                Grid.SetRow(border, period);
                Grid.SetColumn(border, 0);

                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var numText = new TextBlock
                {
                    Text = period.ToString(),
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var timeBlock = new TextBlock
                {
                    Text = timeText ?? string.Empty,
                    FontSize = 12,
                    Foreground = headerTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };

                if (string.IsNullOrWhiteSpace(timeText))
                    timeBlock.Visibility = Visibility.Collapsed;

                stack.Children.Add(numText);
                stack.Children.Add(timeBlock);

                border.Child = stack;
                ScheduleGrid.Children.Add(border);
            }
        }

        private void EnsureScheduleGridInitialized()
        {
            if (_isScheduleGridInitialized)
                return;

            int dayColumnCount = _scheduleWeekRange == 7 ? 7 : 5; // col=1..N

            // 生成课程单元格并绑定通用点击/双击事件（事件逻辑从映射 _courseCellMap 读取当前课程）
            for (int row = 1; row <= _periodCount; row++)
            {
                for (int col = 1; col <= dayColumnCount; col++)
                {
                    var border = new Border
                    {
                        Style = this.Resources["CourseCellStyle"] as Style,
                        Background = new SolidColorBrush(Colors.Transparent),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(4),
                        BorderBrush = null,
                        BorderThickness = new Thickness(0),
                        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                        RenderTransform = new ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 }
                    };

                    border.PointerPressed += CourseCell_PointerPressed;
                    border.DoubleTapped += CourseCell_DoubleTapped;

                    Grid.SetRow(border, row);
                    Grid.SetColumn(border, col);
                    ScheduleGrid.Children.Add(border);
                    _cellMap[(col, row)] = border;
                }
            }

            _isScheduleGridInitialized = true;
        }

        private void ClearAllCourseCells()
        {
            _courseCellMap.Clear();

            foreach (var border in _cellMap.Values)
            {
                if (border == null)
                    continue;

                border.Background = new SolidColorBrush(Colors.Transparent);
                border.Child = null;
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
                border.Visibility = Visibility.Visible;
                Grid.SetRowSpan(border, 1);

                if (border.RenderTransform is ScaleTransform s)
                {
                    s.ScaleX = 0.95;
                    s.ScaleY = 0.95;
                }
            }
        }

        private static List<(int start, int end)> GetConsecutiveSegments(IReadOnlyList<int> sortedDistinctPeriods)
        {
            var segments = new List<(int start, int end)>();
            if (sortedDistinctPeriods == null || sortedDistinctPeriods.Count == 0)
                return segments;

            int segStart = sortedDistinctPeriods[0];
            int prev = sortedDistinctPeriods[0];

            for (int i = 1; i < sortedDistinctPeriods.Count; i++)
            {
                int p = sortedDistinctPeriods[i];
                if (p == prev + 1)
                {
                    prev = p;
                    continue;
                }

                segments.Add((segStart, prev));
                segStart = p;
                prev = p;
            }

            segments.Add((segStart, prev));
            return segments;
        }

        private void ApplyCourseToCells(Course course)
        {
            // 只显示工作日：跳过周六/周日课程
            if (_scheduleWeekRange == 5 &&
                (course.DayOfWeek == DayOfWeek.Saturday || course.DayOfWeek == DayOfWeek.Sunday))
            {
                return;
            }

            int dayOffset = (int)course.DayOfWeek - 1; // Monday=1 -> index 0
            if (dayOffset < 0) dayOffset = 6; // Sunday=7 -> index 6
            int dayCol = dayOffset + 1;

            // 生成连续段：例如 [1,2,3,5] => (1..3), (5..5)
            var sortedDistinctPeriods = course.ClassPeriods?
                .Where(p => p >= 1 && p <= _periodCount)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (sortedDistinctPeriods == null || sortedDistinctPeriods.Count == 0)
                return;

            var segments = GetConsecutiveSegments(sortedDistinctPeriods);
            var courseColor = new SolidColorBrush(ColorHelperFromHex(course.Color));

            foreach (var (startPeriod, endPeriod) in segments)
            {
                var topKey = (dayCol, startPeriod);
                if (!_cellMap.TryGetValue(topKey, out var topBorder) || topBorder == null)
                    continue;

                int spanLen = endPeriod - startPeriod + 1;

                // 只在“连续段的第一节”显示内容，并通过 RowSpan 覆盖中间行
                topBorder.Visibility = Visibility.Visible;
                topBorder.Background = courseColor;
                topBorder.BorderBrush = null;
                topBorder.BorderThickness = new Thickness(0);
                Grid.SetRowSpan(topBorder, spanLen);

                topBorder.Child = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2
                };

                if (topBorder.Child is StackPanel sp)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = course.Name,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold
                    });

                    sp.Children.Add(new TextBlock
                    {
                        Text = course.Teacher,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14
                    });

                    sp.Children.Add(new TextBlock
                    {
                        Text = course.Classroom,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 14
                    });
                }

                _courseCellMap[topBorder] = course;

                // 折叠掉连续段中后续的每节 Border，避免重复显示，也避免出现“分隔缝”
                for (int p = startPeriod + 1; p <= endPeriod; p++)
                {
                    var k = (dayCol, p);
                    if (!_cellMap.TryGetValue(k, out var border) || border == null)
                        continue;

                    border.Visibility = Visibility.Collapsed;
                    border.Background = new SolidColorBrush(Colors.Transparent);
                    border.Child = null;
                    border.BorderBrush = null;
                    border.BorderThickness = new Thickness(0);
                    Grid.SetRowSpan(border, 1);
                }
            }
        }

        private void ResetBorderToEmpty(Border border)
        {
            if (border == null)
                return;

            border.Background = new SolidColorBrush(Colors.Transparent);
            border.Child = null;
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
            border.Visibility = Visibility.Visible;
            Grid.SetRowSpan(border, 1);

            if (border.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 0.95;
                s.ScaleY = 0.95;
            }
        }

        private void ClearCourseCells(Course course)
        {
            foreach (var period in course.ClassPeriods)
            {
                int dayOffset = (int)course.DayOfWeek - 1;
                if (dayOffset < 0) dayOffset = 6;

                var key = (dayOffset + 1, period);
                if (_cellMap.TryGetValue(key, out var border) && border != null)
                {
                    _courseCellMap.Remove(border);
                    ResetBorderToEmpty(border);
                }
            }
        }

        private void CourseCell_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (_courseCellMap.TryGetValue(border, out var course))
            {
                SelectCourse(course, border);
            }
        }

        private async void CourseCell_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (_courseCellMap.TryGetValue(border, out var course))
            {
                await EditCourseByCellAsync(course);
            }
        }

        private Color ColorHelperFromHex(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex) || hex.Length < 7)
                    return Colors.Gray;
                return ColorHelper.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16)
                );
            }
            catch
            {
                return Colors.Gray;
            }
        }

        #region 课程操作

        /// <summary>
        /// 获取当前选中的课程（用于编辑/删除）
        /// </summary>
        public Course? SelectedCourse { get; private set; }
        
        // 记录课程和单元格的映射
        private Dictionary<Border, Course> _courseCellMap = new();

        private async void AddCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowCourseFormAsync(null);
        }

        private async void EditCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCourse == null)
            {
                ShowToast("请先在课程表上点击选择要编辑的课程");
                return;
            }
            await ShowCourseFormAsync(SelectedCourse);
        }

        private async void DeleteCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCourse == null)
            {
                ShowToast("请先在课程表上点击选择要删除的课程");
                return;
            }

            // 确认删除
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除课程 \"{SelectedCourse.Name}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            if (result == ContentDialogResult.Primary)
            {
                await DeleteCourseAsync(SelectedCourse.Id);
                SelectedCourse = null;
                ShowToast("课程已删除");
            }
        }

        private async Task ShowCourseFormAsync(Course? course)
        {
            var dialog = new CourseFormPage(course);
            dialog.XamlRoot = this.XamlRoot;
            
            var result = await ContentDialogGuard.ShowAsync(dialog);

            if (result == ContentDialogResult.Primary && dialog.NewCourse != null)
            {
                bool success = false;
                if (course == null)
                {
                    // 新增
                    success = await AddCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已添加");
                    }
                }
                else
                {
                    // 编辑
                    dialog.NewCourse.Id = course.Id; // 保持原有ID
                    success = await UpdateCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已更新");
                    }
                }
            }
        }

        private async Task EditCourseByCellAsync(Course course)
        {
            SelectedCourse = course;
            await ShowCourseFormAsync(course);
        }

        public async Task<bool> AddCourseAsync(Course course)
        {
            // 冲突检测：同一天、节次重叠（且不是不同“周类型”的完全错开）
            var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: null);
            if (conflict != null)
            {
                await ShowConflictDialogAsync(course, conflict);
                return false;
            }

            _courses.Add(course);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            // 只更新受影响的单元格
            ClearCourseCells(course);
            ApplyCourseToCells(course);
            return true;
        }

        public async Task DeleteCourseAsync(int courseId)
        {
            var target = _courses.Find(c => c.Id == courseId);
            if (target == null)
                return;

            ClearCourseCells(target);
            _courses.RemoveAll(c => c.Id == courseId);
            await CourseDataHelper.SaveCoursesAsync(_courses);
        }

        public async Task<bool> UpdateCourseAsync(Course course)
        {
            var index = _courses.FindIndex(c => c.Id == course.Id);
            if (index >= 0)
            {
                // 冲突检测（排除自身）
                var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: course.Id);
                if (conflict != null)
                {
                    await ShowConflictDialogAsync(course, conflict);
                    return false;
                }

                var oldCourse = _courses[index];
                ClearCourseCells(oldCourse);
                _courses[index] = course;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                ApplyCourseToCells(course);
                return true;
            }

            return false;
        }

        private void ShowToast(string message)
        {
            var toast = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            _ = ContentDialogGuard.ShowAsync(toast);
        }

        // FindConflictCourse 已抽取到 Helpers/CourseConflictHelper.cs

        /// <summary>
        /// 提示用户课程时间冲突
        /// </summary>
        private async Task ShowConflictDialogAsync(Course newCourse, Course conflictCourse)
        {
            var dialog = new ContentDialog
            {
                Title = "时间冲突",
                Content = $"课程 \"{newCourse.Name}\" 的上课时间与已存在的课程 \"{conflictCourse.Name}\" 冲突。\n\n" +
                          "请修改节次或周类型后再保存。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };

            await ContentDialogGuard.ShowAsync(dialog);
        }

        /// <summary>
        /// 切换悬浮栏展开/收起状态
        /// </summary>
        private async void ToggleFloatBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ExpandedPanel.Visibility == Visibility.Collapsed)
            {
                await ExpandPanelAsync();
            }
            else
            {
                await CollapsePanelAsync();
            }
        }

        /// <summary>
        /// 展开操作面板（从右向左伸出）
        /// </summary>
        private async Task ExpandPanelAsync()
        {
            ExpandedPanel.Visibility = Visibility.Visible;
            
            // 获取 ScaleTransform
            var scaleTransform = (ScaleTransform)ExpandedPanel.RenderTransform;
            
            // 使用 XAML Storyboard 动画 ScaleTransform.ScaleX，时长适中（500ms），从 0 → 1
            scaleTransform.ScaleX = 0;
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, scaleTransform);
            Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);
            storyboard.Begin();
            
            // 展开后：让箭头指向右（表示“收起/返回”），且始终以中心旋转
            ToggleIconRotate.Angle = 180;
        }

        /// <summary>
        /// 收起操作面板（从左向右缩回）
        /// </summary>
        private async Task CollapsePanelAsync()
        {
            var scaleTransform = (ScaleTransform)ExpandedPanel.RenderTransform;
            
            // 使用 XAML Storyboard 动画 ScaleTransform.ScaleX，时长适中（500ms），从 1 → 0
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(animation, scaleTransform);
            Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);
            storyboard.Begin();

            // 等待动画结束后再隐藏（与动画时长保持一致）
            await Task.Delay(350);
            ExpandedPanel.Visibility = Visibility.Collapsed;
            scaleTransform.ScaleX = 0;
            
            // 收起后：恢复箭头指向左
            ToggleIconRotate.Angle = 0;
        }

        /// <summary>
        /// 选中课程（点击单元格时调用）
        /// </summary>
        private void SelectCourse(Course course, Border border)
        {
            // 先清除之前的选中状态
            ClearCourseSelection();

            // 设置新的选中状态：略放大，并使用与背景有对比但不刺眼的描边（带动画放大）
            SelectedCourse = course;
            AnimateCourseScale(border, 1.05, 150);

            // 使用系统强调色做半透明描边，兼容浅色/深色
            Color accentColor = Colors.Gray;
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var accentObj) &&
                accentObj is Color accent)
            {
                accentColor = accent;
            }
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B));
            border.BorderThickness = new Thickness(2);
        }

        /// <summary>
        /// 清除当前课程选中状态（用于点击空白区域或切换选中）
        /// </summary>
        private void ClearCourseSelection()
        {
            SelectedCourse = null;

            if (_courseCellMap != null)
            {
                foreach (var cell in _courseCellMap)
                {
                    if (cell.Key != null)
                    {
                        AnimateCourseScale(cell.Key, 0.95, 120);
                        cell.Key.BorderThickness = new Thickness(0);
                        cell.Key.BorderBrush = null;
                    }
                }
            }
        }

        /// <summary>
        /// 为课程单元格执行缩放动画（用于选中/取消选中）
        /// </summary>
        private void AnimateCourseScale(Border border, double targetScale, int durationMs)
        {
            if (border == null)
                return;

            if (border.RenderTransform is not ScaleTransform scaleTransform)
            {
                scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                border.RenderTransform = scaleTransform;
                border.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }

            var storyboard = new Storyboard();

            var animX = new DoubleAnimation
            {
                To = targetScale,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animX, scaleTransform);
            Storyboard.SetTargetProperty(animX, "ScaleX");

            var animY = new DoubleAnimation
            {
                To = targetScale,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animY, scaleTransform);
            Storyboard.SetTargetProperty(animY, "ScaleY");

            storyboard.Children.Add(animX);
            storyboard.Children.Add(animY);
            storyboard.Begin();
        }

        /// <summary>
        /// 点击课程表空白区域时，取消选中
        /// </summary>
        private void ScheduleGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 判断点击是否落在某个课程卡片 Border 上，如果是则不清除选中
            if (e.OriginalSource is DependencyObject source)
            {
                var border = FindParentCourseBorder(source);
                if (border != null && _courseCellMap.ContainsKey(border))
                {
                    // 点击在课程组件内部，保持现有选中逻辑
                    return;
                }
            }

            // 点击在空白区域或非课程元素上，清除选中
            ClearCourseSelection();
        }

        private Border? FindParentCourseBorder(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Border border && _courseCellMap.ContainsKey(border))
                {
                    return border;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        #endregion
    }
}
