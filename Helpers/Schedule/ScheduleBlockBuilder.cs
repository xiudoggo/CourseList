using System;
using System.Collections.Generic;
using System.Linq;
using CourseList.Models;

namespace CourseList.Helpers
{
    public sealed class ScheduleBlock
    {
        public int CourseId { get; init; }
        public Course CourseRef { get; init; } = default!;

        /// <summary>1..5 or 1..7 (Mon..Sun)</summary>
        public int DayCol { get; init; }

        /// <summary>1..PeriodCount</summary>
        public int StartPeriod { get; init; }

        public int Span { get; init; }

        public int EndPeriod => StartPeriod + Math.Max(1, Span) - 1;

        public bool IsActiveInWeek { get; init; }
    }

    public sealed class ScheduleLayoutMetrics
    {
        public double HeaderHeight { get; init; }
        public double PeriodRowHeight { get; init; }
        public double PeriodHeaderColumnWidth { get; init; }
        public double DayColumnWidth { get; init; }

        public double TimeColumnWidth { get; init; }
        public double CompactDayColumnWidth { get; init; }
        public double CompactPeriodRowHeight { get; init; }
    }

    public static class ScheduleBlockBuilder
    {
        public static List<ScheduleBlock> BuildBlocks(
            IReadOnlyList<Course> courses,
            int displayWeek,
            int periodCount,
            int scheduleWeekRange,
            int semesterTotalWeeks,
            IReadOnlyDictionary<(int courseId, int weekIndex), WeekScheduleOverride>? overrideIndex,
            IReadOnlyList<WeekScheduleOverride>? overridesFallback)
        {
            var result = new List<ScheduleBlock>();
            if (courses == null || courses.Count == 0)
                return result;

            int dayColumnCount = scheduleWeekRange == 5 ? 5 : 7;
            periodCount = Math.Max(1, periodCount);

            foreach (var course in courses)
            {
                if (course == null)
                    continue;

                var (effDay, effPeriods) = ScheduleEffectiveHelper.GetEffectiveSlot(
                    course,
                    displayWeek,
                    overrideIndex,
                    overridesFallback);

                // 5-day mode: skip weekends
                if (dayColumnCount == 5 && (effDay == DayOfWeek.Saturday || effDay == DayOfWeek.Sunday))
                    continue;

                int dayCol = DayOfWeekToDayCol(effDay);
                if (dayCol < 1 || dayCol > dayColumnCount)
                    continue;

                var sortedDistinctPeriods = (effPeriods ?? new List<int>())
                    .Where(p => p >= 1 && p <= periodCount)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                if (sortedDistinctPeriods.Count == 0)
                    continue;

                bool isActive = SemesterWeekHelper.IsCourseActiveInWeek(course, displayWeek, semesterTotalWeeks);

                var segments = ScheduleEffectiveHelper.GetConsecutivePeriodSegments(sortedDistinctPeriods);
                foreach (var (start, end) in segments)
                {
                    int span = Math.Max(1, end - start + 1);
                    result.Add(new ScheduleBlock
                    {
                        CourseId = course.Id,
                        CourseRef = course,
                        DayCol = dayCol,
                        StartPeriod = start,
                        Span = span,
                        IsActiveInWeek = isActive
                    });
                }
            }

            // Deterministic order: day, start, then course name
            return result
                .OrderBy(b => b.DayCol)
                .ThenBy(b => b.StartPeriod)
                .ThenBy(b => b.CourseRef?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int DayOfWeekToDayCol(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            DayOfWeek.Sunday => 7,
            _ => 1
        };
    }
}

