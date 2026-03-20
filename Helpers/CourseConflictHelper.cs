using CourseList.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CourseList.Helpers
{
    public static class CourseConflictHelper
    {
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

