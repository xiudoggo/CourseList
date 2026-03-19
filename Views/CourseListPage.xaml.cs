using CourseList.Models;
using CourseList.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CourseList.Views
{
    public sealed partial class CourseListPage : Page
    {
        private List<Course> _courses = new List<Course>();
        private Course? _selectedCourse;

        public CourseListPage()
        {
            this.InitializeComponent();
            Loaded += CourseListPage_Loaded;
        }

        private async void CourseListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCoursesAsync();
        }

        private async Task LoadCoursesAsync()
        {
            _courses = await CourseDataHelper.LoadCoursesAsync();
            CourseRepeater.ItemsSource = _courses;
            
            // 显示/隐藏空状态
            EmptyText.Visibility = _courses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowCourseFormAsync(null);
        }

        private async void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCourse == null)
            {
                ShowToast("请先在列表中点击选择要编辑的课程");
                return;
            }
            await ShowCourseFormAsync(_selectedCourse);
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCourse == null)
            {
                ShowToast("请先在列表中点击选择要删除的课程");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除课程 \"{_selectedCourse.Name}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await DeleteCourseAsync(_selectedCourse.Id);
                _selectedCourse = null;
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
                    await AddCourseAsync(dialog.NewCourse);
                    ShowToast("课程已添加");
                }
                else
                {
                    dialog.NewCourse.Id = course.Id;
                    await UpdateCourseAsync(dialog.NewCourse);
                    ShowToast("课程已更新");
                }
            }
        }

        private async Task AddCourseAsync(Course course)
        {
            _courses.Add(course);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            RefreshList();
        }

        private async Task DeleteCourseAsync(int courseId)
        {
            _courses.RemoveAll(c => c.Id == courseId);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            RefreshList();
        }

        private async Task UpdateCourseAsync(Course course)
        {
            var index = _courses.FindIndex(c => c.Id == course.Id);
            if (index >= 0)
            {
                _courses[index] = course;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                RefreshList();
            }
        }

        private void RefreshList()
        {
            CourseRepeater.ItemsSource = null;
            CourseRepeater.ItemsSource = _courses;
            EmptyText.Visibility = _courses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        /// 选中课程（供外部调用）
        /// </summary>
        public void SelectCourse(Course course)
        {
            _selectedCourse = course;
        }
    }
}
