using System;
using System.Collections.Generic;
using System.Linq;
using CourseList.Models;

namespace CourseList.Helpers
{
    /// <summary>
    /// 解析某周「有效」的星期与节次（含 week-overrides）、连续段拆分与拖放移动辅助。
    /// </summary>
    public static class ScheduleEffectiveHelper
    {
        public static (DayOfWeek DayOfWeek, List<int> Periods) GetEffectiveSlot(
            Course course,
            int weekIndex,
            IReadOnlyList<WeekScheduleOverride>? overrides)
        {
            if (overrides != null)
            {
                foreach (var o in overrides)
                {
                    if (o.CourseId == course.Id && o.WeekIndex == weekIndex)
                    {
                        var periods = (o.ClassPeriods ?? new List<int>())
                            .Where(p => p >= 1)
                            .Distinct()
                            .OrderBy(p => p)
                            .ToList();
                        return (o.DayOfWeek, periods);
                    }
                }
            }

            var basePeriods = (course.ClassPeriods ?? new List<int>())
                .Where(p => p >= 1)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            return (course.DayOfWeek, basePeriods);
        }

        /// <summary>
        /// 使用已索引化的 overrides（O(1) 查找）获取某周有效星期与节次。
        /// </summary>
        public static (DayOfWeek DayOfWeek, List<int> Periods) GetEffectiveSlot(
            Course course,
            int weekIndex,
            IReadOnlyDictionary<(int courseId, int weekIndex), WeekScheduleOverride>? overrideIndex,
            IReadOnlyList<WeekScheduleOverride>? overridesFallback = null)
        {
            if (overrideIndex != null &&
                overrideIndex.TryGetValue((course.Id, weekIndex), out var o) &&
                o != null)
            {
                var periods = (o.ClassPeriods ?? new List<int>())
                    .Where(p => p >= 1)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();
                return (o.DayOfWeek, periods);
            }

            return GetEffectiveSlot(course, weekIndex, overridesFallback);
        }

        public static Dictionary<(int courseId, int weekIndex), WeekScheduleOverride> BuildOverrideIndex(
            IReadOnlyList<WeekScheduleOverride>? overrides)
        {
            var result = new Dictionary<(int courseId, int weekIndex), WeekScheduleOverride>();
            if (overrides == null || overrides.Count == 0)
                return result;

            foreach (var o in overrides)
            {
                if (o == null)
                    continue;
                if (o.CourseId <= 0 || o.WeekIndex <= 0)
                    continue;
                result[(o.CourseId, o.WeekIndex)] = o;
            }

            return result;
        }

        /// <summary>
        /// 尝试将连续段移动到新星期、新起始节；返回新节次列表。若违反单周多段跨天约束则返回 null。
        /// </summary>
        public static List<int>? TryMoveSegment(
            IReadOnlyList<int> sortedDistinctPeriods,
            int segmentStart,
            int segmentEnd,
            int targetStartPeriod,
            int periodCountLimit,
            bool sameWeekdayAsBefore)
        {
            var full = sortedDistinctPeriods.Distinct().OrderBy(p => p).ToList();
            if (full.Count == 0)
                return null;

            int span = segmentEnd - segmentStart + 1;
            if (span < 1)
                return null;

            var remaining = full.Where(p => p < segmentStart || p > segmentEnd).ToList();
            if (targetStartPeriod < 1 || targetStartPeriod + span - 1 > periodCountLimit)
                return null;

            var newRange = Enumerable.Range(targetStartPeriod, span).ToList();
            if (remaining.Any(p => newRange.Contains(p)))
                return null;

            var merged = remaining.Concat(newRange).Distinct().OrderBy(p => p).ToList();

            if (remaining.Count > 0 && !sameWeekdayAsBefore)
                return null;

            return merged;
        }

        /// <summary>
        /// 将节次列表拆成连续段，例如 [1,2,3,5] => (1,3),(5,5)。输入需已去重升序。
        /// </summary>
        public static List<(int Start, int End)> GetConsecutivePeriodSegments(IReadOnlyList<int> sortedDistinctPeriods)
        {
            var segments = new List<(int Start, int End)>();
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
    }

    /// <summary>
    /// 学期起始周一、周序号与课程在某周是否上课（单双周等）。
    /// </summary>
    public static class SemesterWeekHelper
    {
        public static DateTime NormalizeToMonday(DateTime date)
        {
            var d = date.Date;
            int diff = ((int)d.DayOfWeek + 6) % 7;
            return d.AddDays(-diff).Date;
        }

        /// <summary>
        /// 根据学期起始周一计算 date 落在第几周（1-based），与课表页逻辑一致。
        /// </summary>
        public static int GetWeekIndexByDate(DateTime date, DateTime semesterStartMonday)
        {
            var d = date.Date;
            int days = (int)(d - semesterStartMonday.Date).TotalDays;
            return days >= 0 ? (days / 7) + 1 : 1;
        }

        public static bool IsCourseActiveInWeek(Course course, int week, int semesterTotalWeeks)
        {
            int fromWeek = course.FromWeek <= 0 ? 1 : course.FromWeek;
            int toWeek = course.ToWeek <= 0 ? semesterTotalWeeks : course.ToWeek;
            if (fromWeek > toWeek)
                (fromWeek, toWeek) = (toWeek, fromWeek);

            bool inRange = week >= fromWeek && week <= toWeek;
            if (!inRange)
                return false;

            return course.WeekType switch
            {
                1 => week % 2 == 1,
                2 => week % 2 == 0,
                _ => true
            };
        }
    }
}
