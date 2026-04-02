using CourseList.Models;
using CourseList.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace CourseList.Views
{
    public sealed partial class CourseListPage : Page
    {
        private List<Course> _courses = new List<Course>();
        private Course? _selectedCourse;
        private Border? _selectedCardBorder;
        private Thickness _desktopMargin = new Thickness(20);
        private Orientation _desktopFiltersOrientation = Orientation.Horizontal;
        private double _desktopSearchWidth = 260;

        private string _searchText = string.Empty;
        private DayOfWeek? _dayFilter;
        private int? _weekTypeFilter;
        private int? _periodFilter;
        private int _maxPeriods = 11;

        public CourseListPage()
        {
            this.InitializeComponent();

            var config = ConfigHelper.LoadConfig();
            _maxPeriods = config.PeriodCount;
            RebuildPeriodFilterCombo();

            Loaded += CourseListPage_Loaded;
            Unloaded += CourseListPage_Unloaded;
        }

        public void ApplyCompactMode(bool isCompact)
        {
            if (CourseListRootGrid == null || FiltersStackPanel == null)
                return;

            if (isCompact)
            {
                CourseListRootGrid.Margin = new Thickness(12);
                FiltersStackPanel.Visibility = Visibility.Collapsed;

                if (AddBtn != null) AddBtn.Visibility = Visibility.Collapsed;
                if (EditBtn != null) EditBtn.Visibility = Visibility.Collapsed;
                if (DeleteBtn != null) DeleteBtn.Visibility = Visibility.Collapsed;

                if (CourseRepeater != null)
                {
                    // compact 下：只展示“课程长条列表”，布局改为单列竖排。
                    CourseRepeater.Layout = new StackLayout
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 12
                    };

                    if (this.Resources.TryGetValue("CourseCardCompactTemplate", out var compactTpl) &&
                        compactTpl is DataTemplate compactDataTemplate)
                    {
                        CourseRepeater.ItemTemplate = compactDataTemplate;
                    }
                }
            }
            else
            {
                CourseListRootGrid.Margin = _desktopMargin;
                FiltersStackPanel.Visibility = Visibility.Visible;
                FiltersStackPanel.Orientation = _desktopFiltersOrientation;
                FiltersStackPanel.Spacing = 12;

                if (SearchBox != null) SearchBox.Width = _desktopSearchWidth;
                if (DayFilterCombo != null) DayFilterCombo.Width = 140;
                if (WeekTypeFilterCombo != null) WeekTypeFilterCombo.Width = 150;
                if (PeriodFilterCombo != null) PeriodFilterCombo.Width = 140;

                if (CourseRepeater != null)
                {
                    CourseRepeater.Layout = new UniformGridLayout
                    {
                        MinItemWidth = 220,
                        MinItemHeight = 182,
                        MinRowSpacing = 12,
                        MinColumnSpacing = 12,
                        ItemsStretch = UniformGridLayoutItemsStretch.Fill
                    };

                    if (this.Resources.TryGetValue("CourseCardTemplate", out var tpl) &&
                        tpl is DataTemplate dataTemplate)
                    {
                        CourseRepeater.ItemTemplate = dataTemplate;
                    }
                }

                if (AddBtn != null) AddBtn.Visibility = Visibility.Visible;
                if (EditBtn != null) EditBtn.Visibility = Visibility.Visible;
                if (DeleteBtn != null) DeleteBtn.Visibility = Visibility.Visible;
            }
        }

        private void CourseListPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SchemeHelper.SchemeChanged -= OnSchemeChanged;
        }

        private void OnSchemeChanged(object? sender, EventArgs e)
        {
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                var config = ConfigHelper.LoadConfig();
                _maxPeriods = config.PeriodCount;
                RebuildPeriodFilterCombo();
                await LoadCoursesAsync();
            });
        }

        private void RebuildPeriodFilterCombo()
        {
            if (PeriodFilterCombo == null)
                return;

            PeriodFilterCombo.Items.Clear();
            PeriodFilterCombo.Items.Add(new ComboBoxItem { Content = "全部", Tag = "All" });
            for (int i = 1; i <= _maxPeriods; i++)
            {
                PeriodFilterCombo.Items.Add(new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() });
            }

            PeriodFilterCombo.SelectedIndex = 0;
        }

        private async void CourseListPage_Loaded(object sender, RoutedEventArgs e)
        {
            SchemeHelper.SchemeChanged += OnSchemeChanged;
            if (CourseListSelectionTeachingTip != null)
                CourseListSelectionTeachingTip.XamlRoot = XamlRoot;
            await LoadCoursesAsync();
        }

        private async Task LoadCoursesAsync()
        {
            _courses = await CourseDataHelper.LoadCoursesAsync();
            ApplyFiltersToList();
        }

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowCourseFormAsync(null);
        }

        private async void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCourse == null)
            {
                ShowCourseListSelectionTip(EditBtn, "请先在列表中点击选择要编辑的课程");
                return;
            }
            await ShowCourseFormAsync(_selectedCourse);
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCourse == null)
            {
                ShowCourseListSelectionTip(DeleteBtn, "请先在列表中点击选择要删除的课程");
                return;
            }

            if (DeleteConfirmText != null)
                DeleteConfirmText.Text = $"确定要删除课程「{_selectedCourse.Name}」吗？";

            if (sender is FrameworkElement fe)
                FlyoutBase.ShowAttachedFlyout(fe);
        }

        private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmFlyout?.Hide();
        }

        private async void DeleteConfirmOk_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCourse == null)
            {
                DeleteConfirmFlyout?.Hide();
                ShowCourseListSelectionTip(DeleteBtn, "请先在列表中点击选择要删除的课程");
                return;
            }

            int id = _selectedCourse.Id;
            DeleteConfirmFlyout?.Hide();
            await DeleteCourseAsync(id);
            _selectedCourse = null;
            _selectedCardBorder = null;
            ShowToast("课程已删除");
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
                    success = await AddCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已添加");
                    }
                }
                else
                {
                    dialog.NewCourse.Id = course.Id;
                    success = await UpdateCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已更新");
                    }
                }
            }
        }

        private async Task<bool> AddCourseAsync(Course course)
        {
            var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: null);
            if (conflict != null)
            {
                await ShowConflictDialogAsync(course, conflict);
                return false;
            }

            _courses.Add(course);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            RefreshList();
            return true;
        }

        private async Task DeleteCourseAsync(int courseId)
        {
            _courses.RemoveAll(c => c.Id == courseId);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            RefreshList();
        }

        private async Task<bool> UpdateCourseAsync(Course course)
        {
            var index = _courses.FindIndex(c => c.Id == course.Id);
            if (index >= 0)
            {
                var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: course.Id);
                if (conflict != null)
                {
                    await ShowConflictDialogAsync(course, conflict);
                    return false;
                }

                _courses[index] = course;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                RefreshList();
                return true;
            }

            return false;
        }

        private void RefreshList()
        {
            ApplyFiltersToList();
        }

        private void ApplyFiltersToList()
        {
            // XAML 初始化阶段可能会触发 SelectionChanged（例如 SelectedIndex=0）。
            // 这时 x:Name 字段还未完成赋值，因此需要防止空引用崩溃。
            if (CourseRepeater == null || EmptyText == null)
                return;

            // 刷新后旧的 Border 引用已失效，避免对不存在的 UI 做修改
            _selectedCardBorder = null;

            IEnumerable<Course> query = _courses;

            if (_dayFilter.HasValue)
                query = query.Where(c => c.DayOfWeek == _dayFilter.Value);

            if (_weekTypeFilter.HasValue)
                query = query.Where(c => c.WeekType == _weekTypeFilter.Value);

            if (_periodFilter.HasValue)
                query = query.Where(c => c.ClassPeriods.Contains(_periodFilter.Value));

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var s = _searchText.Trim();
                query = query.Where(c =>
                    (!string.IsNullOrEmpty(c.Name) && c.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.Teacher) && c.Teacher.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.Classroom) && c.Classroom.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.Note) && c.Note.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.ScheduleDisplay) && c.ScheduleDisplay.Contains(s, StringComparison.OrdinalIgnoreCase))
                );
            }

            var filtered = query.ToList();

            CourseRepeater.ItemsSource = null;
            CourseRepeater.ItemsSource = filtered;
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text ?? string.Empty;
            ApplyFiltersToList();
        }

        private void FilterCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            // 使用 sender 自身解析变更的 ComboBox，避免依赖字段引用在初始化时为 null 导致崩溃。
            if (sender is not ComboBox combo)
                return;

            var header = combo.Header?.ToString();
            if (combo.SelectedItem is not ComboBoxItem selectedItem)
            {
                ApplyFiltersToList();
                return;
            }

            var tag = selectedItem.Tag as string;
            if (string.IsNullOrEmpty(tag) || tag == "All")
            {
                if (header == "星期")
                    _dayFilter = null;
                else if (header == "周类型")
                    _weekTypeFilter = null;
                else if (header == "包含节次")
                    _periodFilter = null;
                ApplyFiltersToList();
                return;
            }

            if (header == "星期")
            {
                // Tag: Monday/Tuesday/...
                if (Enum.TryParse<DayOfWeek>(tag, out var day))
                    _dayFilter = day;
            }
            else if (header == "周类型")
            {
                // Tag: 0/1/2
                if (int.TryParse(tag, out var weekType))
                    _weekTypeFilter = weekType;
            }
            else if (header == "包含节次")
            {
                // Tag: 1..11
                if (int.TryParse(tag, out var period))
                    _periodFilter = period;
            }

            ApplyFiltersToList();
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

        // FindConflictCourse 已抽取到 Helpers/CourseConflictHelper.cs

        private async Task ShowConflictDialogAsync(Course newCourse, Course conflictCourse)
        {
            var dialog = new ContentDialog
            {
                Title = "时间冲突",
                Content = $"课程 \"{newCourse.Name}\" 的上课时间与已存在的课程 \"{conflictCourse.Name}\" 冲突。\n\n请修改节次或周类型后再保存。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            await ContentDialogGuard.ShowAsync(dialog);
        }

        /// <summary>
        /// 选中课程（供外部调用）
        /// </summary>
        public void SelectCourse(Course course)
        {
            _selectedCourse = course;
        }

        /// <summary>
        /// 列表中点击课程卡片时选中（强调色淡背景 + 轻微抬升，非粗边框）
        /// </summary>
        private void CourseCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (border.Tag is not Course course)
                return;

            if (_selectedCardBorder != null && _selectedCardBorder != border)
                RestoreCourseCardDefaultVisual(_selectedCardBorder);

            _selectedCourse = course;
            _selectedCardBorder = border;
            ApplyCourseCardSelectedVisual(border);
            AnimateCourseCardHoverMetrics(border, 1.03, 0, 100);
        }

        private static void RestoreCourseCardDefaultVisual(Border border)
        {
            float z = border.Name == "CompactRootBorder" ? 10f : 12f;
            ThemeResourceHelper.ApplyDefaultCardChrome(border, border);
            border.BorderThickness = new Thickness(1);
            border.Translation = new Vector3(0, 0, z);
            AnimateCourseCardHoverMetrics(border, 1.0, 0, 0);
        }

        private static void ApplyCourseCardSelectedVisual(Border border)
        {
            float zBase = border.Name == "CompactRootBorder" ? 10f : 12f;
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var accObj) && accObj is Color accent)
                border.Background = new SolidColorBrush(Color.FromArgb(76, accent.R, accent.G, accent.B));
            if (ThemeResourceHelper.TryGetThemeBrush(border, "CourseListCardStrokeBrush", out var st) && st != null)
                border.BorderBrush = st;
            border.BorderThickness = new Thickness(1);
            border.Translation = new Vector3(0, 0, zBase + 10f);
        }

        private void ShowCourseListSelectionTip(FrameworkElement target, string subtitle)
        {
            CourseListSelectionTeachingTip.Target = target;
            CourseListSelectionTeachingTip.Subtitle = subtitle;
            CourseListSelectionTeachingTip.XamlRoot = XamlRoot;
            CourseListSelectionTeachingTip.IsOpen = true;
        }

        private const double CourseHoverLiftY = -9;

        private void CourseCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;
            double scale = ReferenceEquals(border, _selectedCardBorder) ? 1.03 : 1.02;
            AnimateCourseCardHoverMetrics(border, scale, CourseHoverLiftY, 120);
        }

        private void CourseCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            double target = ReferenceEquals(border, _selectedCardBorder) ? 1.03 : 1.0;
            AnimateCourseCardHoverMetrics(border, target, 0, 140);
        }

        private static bool TryGetCourseCardHoverTransforms(Border border, out ScaleTransform? scale, out TranslateTransform? translate)
        {
            scale = null;
            translate = null;
            if (border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                scale = tg.Children[0] as ScaleTransform;
                translate = tg.Children[1] as TranslateTransform;
                return scale != null && translate != null;
            }
            return false;
        }

        private static void AnimateCourseCardHoverMetrics(Border border, double toScale, double toTranslateY, int durationMs)
        {
            if (!TryGetCourseCardHoverTransforms(border, out var st, out var tt) || st == null || tt == null)
                return;

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));

            void Add(DependencyObject target, string prop, double to)
            {
                var anim = new DoubleAnimation { To = to, Duration = dur, EasingFunction = ease };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            }

            Add(st, "ScaleX", toScale);
            Add(st, "ScaleY", toScale);
            Add(tt, "Y", toTranslateY);
            sb.Begin();
        }
    }
}
