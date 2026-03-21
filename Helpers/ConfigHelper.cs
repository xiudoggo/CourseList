using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CourseList.Helpers
{
    
    public class AppConfig
    {
        public string Theme { get; set; } = "Default";
        
        // 关闭窗口时的行为：true = 隐藏到系统托盘；false = 直接关闭退出
        public bool MinimizeToTrayOnClose { get; set; } = false;

        // 是否在关闭窗口时弹出提示：true = 提示；false = 不再提示
        public bool ClosePromptEnabled { get; set; } = true;

        // 课程表显示范围：5=周一到周五，7=周一到周日
        public int ScheduleWeekRange { get; set; } = 7;

        // 每天的节数（默认 11）
        public int PeriodCount { get; set; } = 11;

        // 学期第 1 周的周一日期
        public DateTime SemesterStartMonday { get; set; } = GetCurrentWeekMonday();

        // 学期总周数（用于周次切换与课程起止周上限）
        public int SemesterTotalWeeks { get; set; } = 20;

        // 每节开始与结束时间（长度应与 PeriodCount 对齐；索引 i => 第 i+1 节）
        public List<PeriodTimeRange> PeriodTimeRanges { get; set; } = new List<PeriodTimeRange>();

        private static DateTime GetCurrentWeekMonday()
        {
            var today = DateTime.Today;
            int diff = ((int)today.DayOfWeek + 6) % 7; // Monday=0
            return today.AddDays(-diff).Date;
        }
    }

    [JsonConverter(typeof(PeriodTimeRangeJsonConverter))]
    public class PeriodTimeRange
    {
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }

        public string ToDisplayText()
        {
            if (StartTime.HasValue && EndTime.HasValue)
                return $"{StartTime.Value:HH\\:mm} ~ {EndTime.Value:HH\\:mm}";
            if (StartTime.HasValue)
                return $"{StartTime.Value:HH\\:mm}";
            return string.Empty;
        }

        public static PeriodTimeRange ParseLegacyText(string? text)
        {
            var result = new PeriodTimeRange();
            var value = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return result;

            var unified = value.Replace("～", "~").Replace("—", "-").Replace("－", "-");
            var parts = unified.Split(new[] { "~", "-" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return result;

            if (TryParseTime(parts[0], out var start))
                result.StartTime = start;

            if (parts.Length >= 2 && TryParseTime(parts[1], out var end))
                result.EndTime = end;
            else if (!result.EndTime.HasValue)
                result.EndTime = result.StartTime;

            return result;
        }

        private static bool TryParseTime(string raw, out TimeOnly time)
        {
            var value = (raw ?? string.Empty).Trim();
            if (TimeOnly.TryParse(value, out time))
                return true;

            if (DateTime.TryParse(value, out var dt))
            {
                time = TimeOnly.FromDateTime(dt);
                return true;
            }

            return false;
        }
    }

    public sealed class PeriodTimeRangeJsonConverter : JsonConverter<PeriodTimeRange>
    {
        public override PeriodTimeRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return PeriodTimeRange.ParseLegacyText(reader.GetString());
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Invalid period time range token.");

            var result = new PeriodTimeRange();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return result;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var propName = reader.GetString() ?? string.Empty;
                reader.Read();

                if (string.Equals(propName, "StartTime", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.String &&
                        TimeOnly.TryParse(reader.GetString(), out var start))
                    {
                        result.StartTime = start;
                    }
                }
                else if (string.Equals(propName, "EndTime", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.String &&
                        TimeOnly.TryParse(reader.GetString(), out var end))
                    {
                        result.EndTime = end;
                    }
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, PeriodTimeRange value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.StartTime.HasValue)
                writer.WriteString("StartTime", value.StartTime.Value.ToString("HH\\:mm"));
            else
                writer.WriteNull("StartTime");

            if (value.EndTime.HasValue)
                writer.WriteString("EndTime", value.EndTime.Value.ToString("HH\\:mm"));
            else
                writer.WriteNull("EndTime");
            writer.WriteEndObject();
        }
    }

    public static class ConfigHelper
    {
        private const int MaxPeriodCount = 20;
        private const int MaxSemesterWeeks = 30;

        public static AppConfig LoadConfig()
        {
            try
            {
                SchemeHelper.EnsureMigrated();
                PathHelper.EnsureFolderExists();

                var global = LoadGlobalConfigFromSchemes();
                var scheme = LoadSchemeConfig();
                var merged = MergeConfig(global, scheme);
                return Normalize(merged);
            }
            catch
            {
                return Normalize(new AppConfig());
            }
        }

        private static AppConfig LoadGlobalConfigFromSchemes()
        {
            var global = SchemeHelper.LoadGlobalSettings();
            return new AppConfig
            {
                Theme = global.Theme,
                MinimizeToTrayOnClose = global.MinimizeToTrayOnClose,
                ClosePromptEnabled = global.ClosePromptEnabled
            };
        }

        private static AppConfig LoadSchemeConfig()
        {
            var currentId = SchemeHelper.GetCurrentSchemeId();
            if (string.IsNullOrEmpty(currentId))
                return new AppConfig();
            var path = SchemeHelper.GetSchemeConfigPath(currentId);
            if (!File.Exists(path))
                return new AppConfig();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        private static AppConfig MergeConfig(AppConfig global, AppConfig scheme)
        {
            return new AppConfig
            {
                Theme = global.Theme,
                MinimizeToTrayOnClose = global.MinimizeToTrayOnClose,
                ClosePromptEnabled = global.ClosePromptEnabled,
                ScheduleWeekRange = scheme.ScheduleWeekRange,
                PeriodCount = scheme.PeriodCount,
                SemesterStartMonday = scheme.SemesterStartMonday,
                SemesterTotalWeeks = scheme.SemesterTotalWeeks,
                PeriodTimeRanges = scheme.PeriodTimeRanges ?? new List<PeriodTimeRange>()
            };
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                SchemeHelper.EnsureMigrated();
                PathHelper.EnsureFolderExists();

                var normalized = Normalize(config);

                SchemeHelper.SaveGlobalSettings(new SchemeGlobalSettings
                {
                    Theme = normalized.Theme,
                    MinimizeToTrayOnClose = normalized.MinimizeToTrayOnClose,
                    ClosePromptEnabled = normalized.ClosePromptEnabled
                });

                var currentId = SchemeHelper.GetCurrentSchemeId();
                if (!string.IsNullOrEmpty(currentId))
                {
                    var schemePath = SchemeHelper.GetSchemeConfigPath(currentId);
                    var dir = Path.GetDirectoryName(schemePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var schemeJson = JsonSerializer.Serialize(new
                    {
                        normalized.ScheduleWeekRange,
                        normalized.PeriodCount,
                        normalized.SemesterStartMonday,
                        normalized.SemesterTotalWeeks,
                        normalized.PeriodTimeRanges
                    }, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { new PeriodTimeRangeJsonConverter() }
                    });
                    File.WriteAllText(schemePath, schemeJson);
                }
            }
            catch
            {
                // 可以加日志
            }
        }

        private static AppConfig Normalize(AppConfig config)
        {
            if (config == null)
                config = new AppConfig();

            // 安全范围：1..20
            if (config.PeriodCount < 1)
                config.PeriodCount = 1;
            else if (config.PeriodCount > 20)
                config.PeriodCount = MaxPeriodCount;

            // 学期总周数安全范围：1..30
            if (config.SemesterTotalWeeks < 1)
                config.SemesterTotalWeeks = 1;
            else if (config.SemesterTotalWeeks > MaxSemesterWeeks)
                config.SemesterTotalWeeks = MaxSemesterWeeks;

            // 学期起始日期兜底，并规范到“周一”
            if (config.SemesterStartMonday == default)
                config.SemesterStartMonday = DateTime.Today;
            config.SemesterStartMonday = NormalizeToMonday(config.SemesterStartMonday);

            config.PeriodTimeRanges ??= new List<PeriodTimeRange>();

            // 将可能的 null 元素替换为 ""，避免后续 UI 绑定时报错
            for (int i = 0; i < config.PeriodTimeRanges.Count; i++)
            {
                if (config.PeriodTimeRanges[i] == null)
                    config.PeriodTimeRanges[i] = new PeriodTimeRange();
            }

            // PeriodTimeRanges 永远保持 20 行，缩小节数不应删除数据
            if (config.PeriodTimeRanges.Count < MaxPeriodCount)
            {
                while (config.PeriodTimeRanges.Count < MaxPeriodCount)
                    config.PeriodTimeRanges.Add(new PeriodTimeRange());
            }
            else if (config.PeriodTimeRanges.Count > MaxPeriodCount)
            {
                config.PeriodTimeRanges.RemoveRange(MaxPeriodCount,
                    config.PeriodTimeRanges.Count - MaxPeriodCount);
            }

            return config;
        }

        private static DateTime NormalizeToMonday(DateTime date)
        {
            var d = date.Date;
            int diff = ((int)d.DayOfWeek + 6) % 7; // Monday=0
            return d.AddDays(-diff).Date;
        }
    }
}
