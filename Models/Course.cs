using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CourseList.Models
{
    /// <summary>
    /// 课程数据模型
    /// </summary>
    public class Course
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Teacher { get; set; } = string.Empty;
        public string Classroom { get; set; } = string.Empty;
        public DayOfWeek DayOfWeek { get; set; }
        public List<int> ClassPeriods { get; set; } = new();
        public string Color { get; set; } = string.Empty;
        public int WeekType { get; set; } // 0:全周，1:单周，2:双周
        public string? Note { get; set; }
        
        /// <summary>
        /// 获取课程时间的显示文本
        /// </summary>
        public string ScheduleDisplay
        {
            get
            {
                var dayName = DayOfWeek switch
                {
                    DayOfWeek.Monday => "周一",
                    DayOfWeek.Tuesday => "周二",
                    DayOfWeek.Wednesday => "周三",
                    DayOfWeek.Thursday => "周四",
                    DayOfWeek.Friday => "周五",
                    DayOfWeek.Saturday => "周六",
                    DayOfWeek.Sunday => "周日",
                    _ => "未知"
                };
                
                var periods = string.Join(", ", ClassPeriods.OrderBy(p => p));
                return $"{dayName} 第{periods}节";
            }
        }
    }
}
