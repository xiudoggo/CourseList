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

        // 每节开始与结束时间（长度应与 PeriodCount 对齐；索引 i => 第 i+1 节）
        public List<PeriodTimeRange> PeriodTimeRanges { get; set; } = new List<PeriodTimeRange>();
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
        private static readonly string ConfigFilePath =
            PathHelper.GetFullPath("config.json");

        private const int MaxPeriodCount = 20;

        public static AppConfig LoadConfig()
        {
            try
            {
                PathHelper.EnsureFolderExists();
                if (!File.Exists(ConfigFilePath))
                {
                    var defaultConfig = new AppConfig();
                    SaveConfig(defaultConfig);
                    return defaultConfig;
                }

                string json = File.ReadAllText(ConfigFilePath);
                return Normalize(JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig());
            }
            catch
            {
                // 出错兜底
                return Normalize(new AppConfig());
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                var normalized = Normalize(config);
                string json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(ConfigFilePath, json);
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
    }
}
