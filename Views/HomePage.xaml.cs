using CourseList.Helpers;
using CourseList.Models;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CourseList.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
            Loaded += HomePage_Loaded;
        }

        private void HomePage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = LoadTodaySummaryAsync();
        }

        private async Task LoadTodaySummaryAsync()
        {
            // 1) 星期几
            var today = DateTime.Now.DayOfWeek;
            var weekdayText = today switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                DayOfWeek.Sunday => "星期日",
                _ => "今天"
            };

            TodayWeekdayText.Text = $"今天是{weekdayText}";

            // 2) 今天有几门课
            var courses = await CourseDataHelper.LoadCoursesAsync();
            int count = courses?.Count(c => c.DayOfWeek == today) ?? 0;

            if (count <= 0)
                TodayCourseSummaryText.Text = "今天很轻松";
            else
                TodayCourseSummaryText.Text = $"今天有 {count} 门课";
        }
    }
}