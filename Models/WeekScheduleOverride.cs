using System;
using System.Collections.Generic;

namespace CourseList.Models
{
    /// <summary>
    /// 某门课在指定学期周次的课表覆盖（替代该周的全局 DayOfWeek + ClassPeriods）。
    /// </summary>
    public sealed class WeekScheduleOverride
    {
        public int CourseId { get; set; }

        /// <summary>学期周序号，与课表页「第 N 周」一致。</summary>
        public int WeekIndex { get; set; }

        public DayOfWeek DayOfWeek { get; set; }

        public List<int> ClassPeriods { get; set; } = new();
    }
}
