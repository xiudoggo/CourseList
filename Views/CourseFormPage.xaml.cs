using CourseList.Models;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using Windows.UI;
using Microsoft.UI.Xaml;
using CourseList.Helpers;

namespace CourseList.Views
{
    public sealed partial class CourseFormPage : ContentDialog
    {
        public Course? NewCourse { get; private set; }
        private Course? _editingCourse;
        private string selectedColor = "#2196F3";
        private int _maxPeriods = 11;

        public CourseFormPage(Course? course = null)
        {
            this.InitializeComponent();

            var config = ConfigHelper.LoadConfig();
            _maxPeriods = config.PeriodCount;
            FromWeekBox.Maximum = config.SemesterTotalWeeks;
            ToWeekBox.Maximum = config.SemesterTotalWeeks;
            FromWeekBox.Value = 1;
            ToWeekBox.Value = config.SemesterTotalWeeks;
            
            // 跟随当前主题
            if (App.CurrentMainWindow?.Content is FrameworkElement root)
            {
                this.RequestedTheme = root.RequestedTheme;
            }

            this.PrimaryButtonClick += CourseFormPage_PrimaryButtonClick;
            CourseColorPicker.Color = Microsoft.UI.Colors.Blue;

            // 编辑模式：填充现有数据
            _editingCourse = course;
            if (course != null)
            {
                Title = "编辑课程";
                PrimaryButtonText = "更新";
                LoadCourseData(course);
            }
            else
            {
                Title = "新增课程";
                PrimaryButtonText = "保存";
            }
        }

        private void LoadCourseData(Course course)
        {
            NameBox.Text = course.Name;
            TeacherBox.Text = course.Teacher;
            ClassroomBox.Text = course.Classroom;
            
            // 设置星期
            int dayIndex = (int)course.DayOfWeek - 1;
            if (dayIndex < 0) dayIndex = 6;
            DayCombo.SelectedIndex = dayIndex;

            // 设置节次
            PeriodsBox.Text = string.Join(",", course.ClassPeriods);

            // 设置颜色
            if (!string.IsNullOrEmpty(course.Color))
            {
                selectedColor = course.Color;
                try
                {
                    var color = ColorHelperFromHex(course.Color);
                    CourseColorPicker.Color = ToWindowsColor(color);
                }
                catch { }
            }

            // 设置周类型
            WeekTypeCombo.SelectedIndex = course.WeekType;

            // 设置起止周
            FromWeekBox.Value = course.FromWeek <= 0 ? 1 : course.FromWeek;
            ToWeekBox.Value = course.ToWeek < FromWeekBox.Value ? FromWeekBox.Value : course.ToWeek;

            NoteBox.Text = course.Note ?? "";
        }

        private Color ColorHelperFromHex(string hex)
        {
            return Microsoft.UI.ColorHelper.FromArgb(
                255,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16)
            );
        }

        private Windows.UI.Color ToWindowsColor(Color color)
        {
            return Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        private void CourseFormPage_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) ||
                string.IsNullOrWhiteSpace(PeriodsBox.Text) ||
                DayCombo.SelectedItem == null ||
                WeekTypeCombo.SelectedItem == null)
            {
                args.Cancel = true;
                return;
            }

            try
            {
                var periods = PeriodsBox.Text
                    .Split(new[] { ',', '，', '、', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                    .Where(n => n >= 1 && n <= _maxPeriods)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                if (periods.Count == 0)
                {
                    args.Cancel = true;
                    return;
                }

                var dayTag = ((ComboBoxItem)DayCombo.SelectedItem).Tag as string ?? "Monday";
                if (!Enum.TryParse<DayOfWeek>(dayTag, out var day))
                {
                    day = DayOfWeek.Monday;
                }

                var weekTag = ((ComboBoxItem)WeekTypeCombo.SelectedItem).Tag as string ?? "0";
                if (!int.TryParse(weekTag, out var weekType))
                {
                    weekType = 0;
                }

                int fromWeek = (int)Math.Round(FromWeekBox.Value);
                int toWeek = (int)Math.Round(ToWeekBox.Value);
                int maxWeek = (int)Math.Round(FromWeekBox.Maximum);
                if (maxWeek <= 0) maxWeek = 20;
                fromWeek = Math.Clamp(fromWeek, 1, maxWeek);
                toWeek = Math.Clamp(toWeek, 1, maxWeek);
                if (fromWeek > toWeek)
                {
                    args.Cancel = true;
                    return;
                }

                NewCourse = new Course
                {
                    Id = _editingCourse?.Id ?? new Random().Next(1, 100000),
                    Name = NameBox.Text,
                    Teacher = TeacherBox.Text,
                    Classroom = ClassroomBox.Text,
                    DayOfWeek = day,
                    ClassPeriods = periods,
                    Color = selectedColor,
                    WeekType = weekType,
                    FromWeek = fromWeek,
                    ToWeek = toWeek,
                    Note = NoteBox.Text
                };
            }
            catch
            {
                args.Cancel = true;
            }
        }

        private void CourseColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            var color = args.NewColor;
            selectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
