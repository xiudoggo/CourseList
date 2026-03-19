using CourseList.Models;
using CourseList.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;

namespace CourseList.Views
{
    public sealed partial class SchedulePage : Page
    {
        private List<Course> _courses = new List<Course>();
        private Dictionary<(int day, int period), Border> _cellMap = new();

        public SchedulePage()
        {
            this.InitializeComponent();
            Loaded += SchedulePage_Loaded;
            
            // 初始化时隐藏操作面板
            ExpandedPanel.Visibility = Visibility.Collapsed;
        }

        private async void SchedulePage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCoursesAsync();
            BuildScheduleGrid();
        }

        private async Task LoadCoursesAsync()
        {
            _courses = await CourseDataHelper.LoadCoursesAsync();
        }

        private void BuildScheduleGrid()
        {
            // 先清空课程单元格（保留标题和节次）
            foreach (var cell in _cellMap.Values)
            {
                if (cell != null && ScheduleGrid.Children.Contains(cell))
                {
                    ScheduleGrid.Children.Remove(cell);
                }
            }
            _cellMap.Clear();

            // 生成课程单元格并填充课程
            for (int row = 1; row <= 11; row++)
            {
                for (int col = 1; col <= 7; col++)
                {
                    var border = new Border
                    {
                        Style = this.Resources["CourseCellStyle"] as Style,
                        Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128))
                    };
                    Grid.SetRow(border, row);
                    Grid.SetColumn(border, col);
                    ScheduleGrid.Children.Add(border);
                    _cellMap[(col, row)] = border;
                }
            }

            // 填充课程
            foreach (var course in _courses)
            {
                foreach (var period in course.ClassPeriods)
                {
                    int dayOffset = (int)course.DayOfWeek - 1; // Monday=1 -> index 0
                    if (dayOffset < 0) dayOffset = 6; // Sunday = 7 -> index 6
                    
                    var key = (dayOffset + 1, period);
                    if (_cellMap.TryGetValue(key, out var border) && border != null)
                    {
                        border.Background = new SolidColorBrush(ColorHelperFromHex(course.Color));
                        border.BorderBrush = new SolidColorBrush(Colors.White);
                        border.BorderThickness = new Thickness(2);
                        
                        // 点击选中课程
                        border.PointerPressed += (s, e) => SelectCourse(course, border);
                        
                        // 双击编辑课程
                        border.DoubleTapped += async (s, e) => await EditCourseByCellAsync(course);
                        
                        // 保存映射
                        _courseCellMap[border] = course;
                        
                        border.Child = new TextBlock
                        {
                            Text = $"{course.Name}\n{course.Classroom}",
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Colors.White),
                            FontSize = 12
                        };
                    }
                }
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

            var result = await dialog.ShowAsync();
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
            
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.NewCourse != null)
            {
                if (course == null)
                {
                    // 新增
                    await AddCourseAsync(dialog.NewCourse);
                    ShowToast("课程已添加");
                }
                else
                {
                    // 编辑
                    dialog.NewCourse.Id = course.Id; // 保持原有ID
                    await UpdateCourseAsync(dialog.NewCourse);
                    ShowToast("课程已更新");
                }
            }
        }

        private async Task EditCourseByCellAsync(Course course)
        {
            SelectedCourse = course;
            await ShowCourseFormAsync(course);
        }

        public async Task AddCourseAsync(Course course)
        {
            _courses.Add(course);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            BuildScheduleGrid();
        }

        public async Task DeleteCourseAsync(int courseId)
        {
            _courses.RemoveAll(c => c.Id == courseId);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            BuildScheduleGrid();
        }

        public async Task UpdateCourseAsync(Course course)
        {
            var index = _courses.FindIndex(c => c.Id == course.Id);
            if (index >= 0)
            {
                _courses[index] = course;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                BuildScheduleGrid();
            }
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
            _ = toast.ShowAsync();
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
            
            try
            {
                // 创建动画
                var visual = ElementCompositionPreview.GetElementVisual(ExpandedPanel);
                var compositor = visual.Compositor;

                // 创建缩放动画
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.Target = "Scale";
                scaleAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 1, 1));
                scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(1, 1, 1));
                scaleAnimation.Duration = TimeSpan.FromMilliseconds(300);
                
                // 设置动画插值器（使动画更流畅）
                var cubicBezier = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(0.2f, 1f));
                scaleAnimation.InsertKeyFrame(0.7f, new System.Numerics.Vector3(1.05f, 1, 1), cubicBezier);
                
                // 在 UIElement 上启动动画
                visual.StartAnimation("Scale.X", scaleAnimation);
            }
            catch
            {
                // 如果动画失败，直接显示
                scaleTransform.ScaleX = 1;
            }
            
            // 旋转箭头到 90 度（指向下）
            var arrowIcon = (FontIcon)ToggleFloatBtn.Content;
            arrowIcon.RenderTransform = new RotateTransform { Angle = 90 };
        }

        /// <summary>
        /// 收起操作面板（从左向右缩回）
        /// </summary>
        private async Task CollapsePanelAsync()
        {
            var scaleTransform = (ScaleTransform)ExpandedPanel.RenderTransform;
            
            try
            {
                // 创建动画
                var visual = ElementCompositionPreview.GetElementVisual(ExpandedPanel);
                var compositor = visual.Compositor;

                // 创建缩放动画
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.Target = "Scale";
                scaleAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(1, 1, 1));
                scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 1, 1));
                scaleAnimation.Duration = TimeSpan.FromMilliseconds(200);
                
                // 在 UIElement 上启动动画
                visual.StartAnimation("Scale.X", scaleAnimation);
                
                // 等待动画完成后隐藏
                await Task.Delay(200);
                ExpandedPanel.Visibility = Visibility.Collapsed;
                scaleTransform.ScaleX = 0; // 重置缩放值
            }
            catch
            {
                // 如果动画失败，直接隐藏
                ExpandedPanel.Visibility = Visibility.Collapsed;
                scaleTransform.ScaleX = 0;
            }
            
            // 重置箭头方向（指向左）
            var arrowIcon = (FontIcon)ToggleFloatBtn.Content;
            arrowIcon.RenderTransform = null;
        }

        /// <summary>
        /// 选中课程（点击单元格时调用）
        /// </summary>
        private void SelectCourse(Course course, Border border)
        {
            // 先清除之前的选中状态
            if (_courseCellMap != null)
            {
                foreach (var cell in _courseCellMap)
                {
                    if (cell.Key != null)
                    {
                        cell.Key.BorderBrush = new SolidColorBrush(Colors.White);
                        cell.Key.BorderThickness = new Thickness(2);
                    }
                }
            }
            
            // 设置新的选中状态
            SelectedCourse = course;
            border.BorderBrush = new SolidColorBrush(Colors.Yellow);
            border.BorderThickness = new Thickness(3);
        }

        #endregion
    }
}
