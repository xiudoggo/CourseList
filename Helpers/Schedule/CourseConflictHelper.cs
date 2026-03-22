using CourseList.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CourseList.Helpers
{
    public static class CourseConflictHelper
    {
        /// <summary>
        /// 在指定学期周次下，按「有效课表」（含周覆盖）检测 candidate 是否与某门课冲突。
        /// candidate 应为假设排课后的临时对象（DayOfWeek + ClassPeriods）。
        /// </summary>
        public static Course? FindConflictCourseForWeek(
            IEnumerable<Course> existingCourses,
            Course candidate,
            int weekIndex,
            int semesterTotalWeeks,
            int? excludeCourseId,
            IReadOnlyList<WeekScheduleOverride>? overrides)
        {
            if (existingCourses == null)
                return null;

            overrides ??= Array.Empty<WeekScheduleOverride>();

            foreach (var existing in existingCourses)
            {
                if (excludeCourseId.HasValue && existing.Id == excludeCourseId.Value)
                    continue;

                var (existingDay, existingPeriods) = ScheduleEffectiveHelper.GetEffectiveSlot(existing, weekIndex, overrides);
                if (existingDay != candidate.DayOfWeek)
                    continue;

                int existingFrom = existing.FromWeek <= 0 ? 1 : existing.FromWeek;
                int existingTo = existing.ToWeek <= 0 ? int.MaxValue : existing.ToWeek;
                int newFrom = candidate.FromWeek <= 0 ? 1 : candidate.FromWeek;
                int newTo = candidate.ToWeek <= 0 ? int.MaxValue : candidate.ToWeek;
                bool hasWeekOverlap = existingFrom <= newTo && newFrom <= existingTo;
                if (!hasWeekOverlap)
                    continue;

                if (existing.WeekType != 0 && candidate.WeekType != 0 && existing.WeekType != candidate.WeekType)
                    continue;

                if (!SemesterWeekHelper.IsCourseActiveInWeek(existing, weekIndex, semesterTotalWeeks))
                    continue;

                foreach (var p in candidate.ClassPeriods ?? Enumerable.Empty<int>())
                {
                    if (existingPeriods.Contains(p))
                        return existing;
                }
            }

            return null;
        }

        /// <summary>
        /// 查找与 newCourse 在“时间上”冲突的课程（返回第一门冲突课程，不返回则为 null）。
        /// 冲突规则与原 SchedulePage / CourseListPage 保持一致：
        /// - 同一天（DayOfWeek 相同）
        /// - 有节次交集
        /// - 周类型：
        ///   - 若其中任意一门是全周(0)，则认为会冲突（只要节次相交）
        ///   - 若都不是全周且周类型不同，则不冲突
        /// </summary>
        public static Course? FindConflictCourse(IEnumerable<Course> existingCourses, Course newCourse, int? excludeCourseId = null)
        {
            if (existingCourses == null)
                return null;

            foreach (var existing in existingCourses)
            {
                if (excludeCourseId.HasValue && existing.Id == excludeCourseId.Value)
                    continue;

                if (existing.DayOfWeek != newCourse.DayOfWeek)
                    continue;

                // 周次范围无交集：不冲突
                int existingFrom = existing.FromWeek <= 0 ? 1 : existing.FromWeek;
                int existingTo = existing.ToWeek <= 0 ? int.MaxValue : existing.ToWeek;
                int newFrom = newCourse.FromWeek <= 0 ? 1 : newCourse.FromWeek;
                int newTo = newCourse.ToWeek <= 0 ? int.MaxValue : newCourse.ToWeek;
                bool hasWeekOverlap = existingFrom <= newTo && newFrom <= existingTo;
                if (!hasWeekOverlap)
                    continue;

                // 都不是“全周”且周类型不同：允许共用
                if (existing.WeekType != 0 && newCourse.WeekType != 0 && existing.WeekType != newCourse.WeekType)
                    continue;

                // 节次有交集则冲突
                foreach (var p in newCourse.ClassPeriods)
                {
                    if (existing.ClassPeriods.Contains(p))
                        return existing;
                }
            }

            return null;
        }
    }
}

