using CourseList.Helpers;
using CourseList.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI;

namespace CourseList.Views
{
    public class TodayCourseCardItem
    {
        public string Name { get; set; } = string.Empty;
        public string Teacher { get; set; } = string.Empty;
        public string Classroom { get; set; } = string.Empty;
        public string TeacherClassroomText { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
        public int StartPeriod { get; set; }
        public SolidColorBrush ColorBrush { get; set; } = new SolidColorBrush(Colors.Gray);
    }

    public sealed partial class HomePage : Page
    {
        private readonly ObservableCollection<TodayCourseCardItem> _todayCourseCards = new();

        public HomePage()
        {
            this.InitializeComponent();
            Loaded += HomePage_Loaded;
            TodayCourseRepeater.ItemsSource = _todayCourseCards;
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

            // 2) 今天课程卡片（按连续节次分段，不连续则拆成多张）
            var courses = await CourseDataHelper.LoadCoursesAsync();
            var config = ConfigHelper.LoadConfig();
            var cards = BuildTodayCourseCards(courses ?? new List<Course>(), config, today);

            _todayCourseCards.Clear();
            foreach (var card in cards)
            {
                _todayCourseCards.Add(card);
            }

            if (_todayCourseCards.Count <= 0)
            {
                TodayCourseSummaryText.Text = "今天很轻松";
                TodayEmptyText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }
            else
            {
                TodayCourseSummaryText.Text = $"今天有 {_todayCourseCards.Count} 节课程安排";
                TodayEmptyText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        }

        private static List<TodayCourseCardItem> BuildTodayCourseCards(List<Course> allCourses, AppConfig config, DayOfWeek today)
        {
            var result = new List<TodayCourseCardItem>();
            int periodCount = Math.Max(1, config?.PeriodCount ?? 11);
            var periodTimeRanges = config?.PeriodTimeRanges ?? new List<PeriodTimeRange>();

            var todayCourses = allCourses
                .Where(c => c.DayOfWeek == today)
                .ToList();

            foreach (var course in todayCourses)
            {
                var sortedPeriods = course.ClassPeriods
                    .Where(p => p >= 1 && p <= periodCount)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                if (sortedPeriods.Count == 0)
                    continue;

                foreach (var (start, end) in GetConsecutiveSegments(sortedPeriods))
                {
                    var periodText = start == end ? $"第{start}节" : $"第{start}~{end}节";
                    var timeText = BuildCompactTimeText(periodTimeRanges, start, end, periodText);

                    result.Add(new TodayCourseCardItem
                    {
                        Name = course.Name,
                        Teacher = course.Teacher,
                        Classroom = course.Classroom,
                        TeacherClassroomText = $"{course.Teacher} · {course.Classroom}",
                        TimeText = timeText,
                        StartPeriod = start,
                        ColorBrush = new SolidColorBrush(ParseCourseColor(course.Color))
                    });
                }
            }

            return result.OrderBy(c => c.StartPeriod).ThenBy(c => c.Name).ToList();
        }

        private static string GetPeriodText(List<PeriodTimeRange> periodTimeRanges, int period)
        {
            int index = period - 1;
            if (index < 0 || index >= periodTimeRanges.Count)
                return string.Empty;

            return periodTimeRanges[index]?.ToDisplayText() ?? string.Empty;
        }

        private static Color ParseCourseColor(string? hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7 || hex[0] != '#')
                    return Colors.Gray;

                return ColorHelper.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16));
            }
            catch
            {
                return Colors.Gray;
            }
        }

        private static string BuildCompactTimeText(List<PeriodTimeRange> periodTimeRanges, int startPeriod, int endPeriod, string fallbackPeriodText)
        {
            var startRange = GetPeriodText(periodTimeRanges, startPeriod);
            var endRange = GetPeriodText(periodTimeRanges, endPeriod);

            var startClock = ExtractFirstClockTime(startRange);
            var endClock = ExtractLastClockTime(endRange);

            if (!string.IsNullOrWhiteSpace(startClock) && !string.IsNullOrWhiteSpace(endClock))
                return startClock == endClock ? startClock : $"{startClock} ~ {endClock}";

            if (!string.IsNullOrWhiteSpace(startClock))
                return startClock;

            return fallbackPeriodText;
        }

        private static string ExtractFirstClockTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var match = Regex.Match(text, @"\b(\d{1,2}):(\d{2})\b");
            if (!match.Success)
                return string.Empty;

            return NormalizeClock(match.Groups[1].Value, match.Groups[2].Value);
        }

        private static string ExtractLastClockTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var matches = Regex.Matches(text, @"\b(\d{1,2}):(\d{2})\b");
            if (matches.Count == 0)
                return string.Empty;

            var last = matches[matches.Count - 1];
            return NormalizeClock(last.Groups[1].Value, last.Groups[2].Value);
        }

        private static string NormalizeClock(string hourRaw, string minuteRaw)
        {
            if (!int.TryParse(hourRaw, out var hour))
                return string.Empty;
            if (!int.TryParse(minuteRaw, out var minute))
                return string.Empty;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return string.Empty;

            return $"{hour:00}:{minute:00}";
        }

        private static List<(int start, int end)> GetConsecutiveSegments(IReadOnlyList<int> sortedPeriods)
        {
            var segments = new List<(int start, int end)>();
            if (sortedPeriods == null || sortedPeriods.Count == 0)
                return segments;

            int segStart = sortedPeriods[0];
            int prev = sortedPeriods[0];
            for (int i = 1; i < sortedPeriods.Count; i++)
            {
                int current = sortedPeriods[i];
                if (current == prev + 1)
                {
                    prev = current;
                    continue;
                }

                segments.Add((segStart, prev));
                segStart = current;
                prev = current;
            }

            segments.Add((segStart, prev));
            return segments;
        }
    }
}