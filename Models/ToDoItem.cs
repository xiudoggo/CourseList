using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI;

namespace CourseList.Models
{
    /// <summary>
    /// 待办优先级（7 档，数值越大越紧急）
    /// </summary>
    public enum ToDoPriority
    {
        Lowest = 0,
        VeryLow = 1,
        Low = 2,
        Medium = 3,
        High = 4,
        VeryHigh = 5,
        Highest = 6
    }

    /// <summary>
    /// JSON：新数据存字符串枚举名；历史数字 0–4 按旧 5 档映射到新枚举。
    /// </summary>
    public sealed class ToDoPriorityJsonConverter : JsonConverter<ToDoPriority>
    {
        // 旧枚举：Low=0, Medium=1, High=2, Urgent=3, Relaxed=4
        private static readonly ToDoPriority[] LegacyNumeric0To4 =
        {
            ToDoPriority.Low,     // 旧 Low
            ToDoPriority.Medium,
            ToDoPriority.High,
            ToDoPriority.Highest, // 旧 Urgent
            ToDoPriority.Lowest   // 旧 Relaxed
        };

        public override ToDoPriority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (string.IsNullOrEmpty(s))
                    return ToDoPriority.Medium;
                return Enum.TryParse<ToDoPriority>(s, ignoreCase: true, out var p) ? p : ToDoPriority.Medium;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                var v = reader.GetInt32();
                if (v >= 0 && v <= 4)
                    return LegacyNumeric0To4[v];
                if (v == 5) return ToDoPriority.VeryHigh;
                if (v == 6) return ToDoPriority.Highest;
                return ToDoPriority.Medium;
            }

            return ToDoPriority.Medium;
        }

        public override void Write(Utf8JsonWriter writer, ToDoPriority value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// 待办数据模型
    /// </summary>
    public class ToDoItem : INotifyPropertyChanged
    {
        /// <summary>用于数据的唯一识别和操作</summary>
        public int Id { get; set; }

        /// <summary>任务标题（title/Title）</summary>
        public string Title { get; set; } = string.Empty;

        private bool _completed;

        /// <summary>完成状态（completed/Completed）</summary>
        public bool Completed
        {
            get => _completed;
            set
            {
                if (_completed == value)
                    return;
                _completed = value;
                OnPropertyChanged(nameof(Completed));
            }
        }

        /// <summary>创建时间（createdAt）</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>内容</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>截止日期（dueDate）</summary>
        public DateTime? DueDate { get; set; }

        private ToDoPriority _priority = ToDoPriority.Medium;

        /// <summary>优先级（priority）</summary>
        [JsonConverter(typeof(ToDoPriorityJsonConverter))]
        public ToDoPriority Priority
        {
            get => _priority;
            set
            {
                if (_priority == value)
                    return;
                _priority = value;
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(PriorityDisplayName));
                OnPropertyChanged(nameof(PriorityEndColor));
            }
        }

        /// <summary>更新时间（updatedAt）</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>标签/分类（tags）</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// 分类别名（Categories），与 Tags 指向同一集合语义。
        /// </summary>
        public List<string> Categories
        {
            get => Tags;
            set => Tags = value ?? new List<string>();
        }

        [JsonIgnore]
        public string DueDateText => DueDate.HasValue ? $"截止：{DueDate:yyyy-MM-dd}" : "截止：未设置";

        /// <summary>列表徽章：中文优先级名称。</summary>
        [JsonIgnore]
        public string PriorityDisplayName => GetPriorityDisplayName(Priority);

        /// <summary>优先级徽章用色（绿→红，7 档等间隔）。</summary>
        [JsonIgnore]
        public Color PriorityEndColor => GetPriorityColor(Priority);

        /// <summary>列表拖拽时占位的空槽（不入库）。</summary>
        [JsonIgnore]
        public bool IsDragPlaceholder { get; set; }

        public static ToDoItem CreateDragPlaceholder() => new() { IsDragPlaceholder = true };

        /// <summary>中文优先级文案（供列表、表单等复用）。</summary>
        public static string GetPriorityDisplayName(ToDoPriority p) => p switch
        {
            ToDoPriority.Lowest => "最低",
            ToDoPriority.VeryLow => "极低",
            ToDoPriority.Low => "低",
            ToDoPriority.Medium => "中",
            ToDoPriority.High => "高",
            ToDoPriority.VeryHigh => "极高",
            ToDoPriority.Highest => "最高",
            _ => "中"
        };

        /// <summary>
        /// 在绿色与红色之间按 7 档等间隔取色（RGB 线性插值）。
        /// </summary>
        public static Color GetPriorityColor(ToDoPriority p)
        {
            int i = (int)p;
            if (i < 0) i = 0;
            if (i > 6) i = 6;
            const float max = 6f;
            float t = i / max;
            byte r = (byte)(0 + t * 255);
            byte g = (byte)(200 * (1 - t));
            byte b = (byte)(64 * (1 - t));
            return Color.FromArgb(255, r, g, b);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
