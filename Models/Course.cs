using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

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
        public int FromWeek { get; set; } = 1;
        public int ToWeek { get; set; } = 20;
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

        // ====== UI 辅助：每行最多25个字符换行 ======
        // 这些属性仅用于课程列表卡片显示，不参与 JSON 持久化。
        [JsonIgnore]
        public string NameWrapped => WrapEvery25(Name);
        [JsonIgnore]
        public string TeacherWrapped => WrapEvery25(Teacher);
        [JsonIgnore]
        public string ClassroomWrapped => WrapEvery25(Classroom);
        [JsonIgnore]
        public string ScheduleDisplayWrapped => WrapEvery25(ScheduleDisplay);
        [JsonIgnore]
        public string NoteWrapped => WrapEvery25(Note);

        /// <summary>用于列表卡片色条绑定（#RRGGBB），解析失败时使用默认蓝。</summary>
        [JsonIgnore]
        public Windows.UI.Color UiColor => ParseHexToUiColor(Color);

        private static Windows.UI.Color ParseHexToUiColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7 || hex[0] != '#')
                return Windows.UI.Color.FromArgb(255, 33, 150, 243);

            try
            {
                return Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16));
            }
            catch
            {
                return Windows.UI.Color.FromArgb(255, 33, 150, 243);
            }
        }

        private static string WrapEvery25(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            const int max = 25;
            // 按字符下标插入 '\n'，TextBlock 的 Text 属性会呈现成多行。
            var parts = new List<string>();
            for (int i = 0; i < input.Length; i += max)
            {
                var len = Math.Min(max, input.Length - i);
                parts.Add(input.Substring(i, len));
            }
            return string.Join("\n", parts);
        }
    }
}
