using CourseList.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CourseList.Helpers
{
    public sealed class CourseImportParseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<Course> Courses { get; set; } = new();
        public int RawItemCount { get; set; }
    }

    internal sealed class ImportRow
    {
        public int day { get; set; }
        public int begin { get; set; }
        public int end { get; set; }
        public string name { get; set; } = string.Empty;
        public string teacher { get; set; } = string.Empty;
        public string roomText { get; set; } = string.Empty;
        public string color { get; set; } = string.Empty;
    }

    internal sealed class ImportPayload
    {
        public bool ok { get; set; }
        public string error { get; set; } = string.Empty;
        public int count { get; set; }
        public List<ImportRow> rows { get; set; } = new();
    }

    internal sealed class AggregatedCourse
    {
        public string Name { get; set; } = string.Empty;
        public string Teacher { get; set; } = string.Empty;
        public string Classroom { get; set; } = string.Empty;
        public DayOfWeek DayOfWeek { get; set; }
        public string Color { get; set; } = string.Empty;
        public int WeekType { get; set; }
        public int FromWeek { get; set; }
        public int ToWeek { get; set; }
        public HashSet<int> Periods { get; set; } = new();
    }

    public static class CourseImportParser
    {
        public static CourseImportParseResult ParseFromExecuteScriptResult(string executeScriptRawResult, int totalWeeks)
        {
            try
            {
                var jsonPayload = JsonSerializer.Deserialize<string>(executeScriptRawResult) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(jsonPayload))
                {
                    return new CourseImportParseResult
                    {
                        Success = false,
                        ErrorMessage = "脚本返回为空，未检测到课程表。"
                    };
                }

                var payload = JsonSerializer.Deserialize<ImportPayload>(jsonPayload);
                if (payload == null)
                {
                    return new CourseImportParseResult
                    {
                        Success = false,
                        ErrorMessage = "脚本结果格式不正确。"
                    };
                }

                if (!payload.ok)
                {
                    return new CourseImportParseResult
                    {
                        Success = false,
                        ErrorMessage = string.IsNullOrWhiteSpace(payload.error) ? "当前页面不是可导入的课程表页面。" : payload.error
                    };
                }

                var courses = BuildCourses(payload.rows ?? new List<ImportRow>(), totalWeeks);
                return new CourseImportParseResult
                {
                    Success = true,
                    Courses = courses,
                    RawItemCount = payload.count
                };
            }
            catch (Exception ex)
            {
                return new CourseImportParseResult
                {
                    Success = false,
                    ErrorMessage = $"解析失败：{ex.Message}"
                };
            }
        }

        private static List<Course> BuildCourses(List<ImportRow> rows, int totalWeeks)
        {
            var map = new Dictionary<string, AggregatedCourse>();
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.name))
                    continue;

                var dayOfWeek = ConvertDay(row.day);
                if (dayOfWeek == null)
                    continue;

                ParseRoomInfo(row.roomText, totalWeeks, out var fromWeek, out var toWeek, out var weekType, out var classroom);
                var normalizedName = NormalizeCourseName(row.name);
                var normalizedTeacher = NormalizeTeacher(row.teacher);
                var normalizedColor = NormalizeColor(row.color);
                if (row.begin <= 0 || row.end <= 0)
                    continue;

                var key = $"{normalizedName}|{normalizedTeacher}|{classroom}|{(int)dayOfWeek.Value}|{fromWeek}|{toWeek}|{weekType}|{normalizedColor}";
                if (!map.TryGetValue(key, out var aggr))
                {
                    aggr = new AggregatedCourse
                    {
                        Name = normalizedName,
                        Teacher = normalizedTeacher,
                        Classroom = classroom,
                        DayOfWeek = dayOfWeek.Value,
                        Color = normalizedColor,
                        WeekType = weekType,
                        FromWeek = fromWeek,
                        ToWeek = toWeek
                    };
                    map[key] = aggr;
                }

                var start = Math.Min(row.begin, row.end);
                var end = Math.Max(row.begin, row.end);
                for (int p = start; p <= end; p++)
                    aggr.Periods.Add(p);
            }

            var random = new Random();
            var result = new List<Course>();
            foreach (var aggr in map.Values)
            {
                result.Add(new Course
                {
                    Id = GenerateCourseId(random),
                    Name = aggr.Name,
                    Teacher = aggr.Teacher,
                    Classroom = aggr.Classroom,
                    DayOfWeek = aggr.DayOfWeek,
                    ClassPeriods = aggr.Periods.OrderBy(x => x).ToList(),
                    Color = aggr.Color,
                    WeekType = aggr.WeekType,
                    FromWeek = aggr.FromWeek,
                    ToWeek = aggr.ToWeek
                });
            }

            return result
                .OrderBy(c => (int)c.DayOfWeek)
                .ThenBy(c => c.ClassPeriods.FirstOrDefault())
                .ThenBy(c => c.Name)
                .ToList();
        }

        private static int GenerateCourseId(Random random)
        {
            return random.Next(10000, 99999);
        }

        private static DayOfWeek? ConvertDay(int day)
        {
            return day switch
            {
                1 => DayOfWeek.Monday,
                2 => DayOfWeek.Tuesday,
                3 => DayOfWeek.Wednesday,
                4 => DayOfWeek.Thursday,
                5 => DayOfWeek.Friday,
                6 => DayOfWeek.Saturday,
                7 => DayOfWeek.Sunday,
                _ => null
            };
        }

        private static void ParseRoomInfo(string roomText, int totalWeeks, out int fromWeek, out int toWeek, out int weekType, out string classroom)
        {
            fromWeek = 1;
            toWeek = Math.Max(1, totalWeeks);
            weekType = 0;
            classroom = string.Empty;

            var text = (roomText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var weekMatch = Regex.Match(text, @"(?<from>\d+)\s*-\s*(?<to>\d+)\s*周(?:\((?<type>单|双)\))?");
            if (weekMatch.Success)
            {
                fromWeek = SafeInt(weekMatch.Groups["from"].Value, 1);
                toWeek = SafeInt(weekMatch.Groups["to"].Value, toWeek);
                if (fromWeek > toWeek)
                    (fromWeek, toWeek) = (toWeek, fromWeek);

                var type = weekMatch.Groups["type"].Value;
                weekType = type switch
                {
                    "单" => 1,
                    "双" => 2,
                    _ => 0
                };
            }

            fromWeek = Math.Clamp(fromWeek, 1, Math.Max(1, totalWeeks));
            toWeek = Math.Clamp(toWeek, 1, Math.Max(1, totalWeeks));
            if (fromWeek > toWeek)
                (fromWeek, toWeek) = (toWeek, fromWeek);

            var parts = text.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                classroom = parts[^1].Trim();
        }

        private static int SafeInt(string raw, int fallback)
        {
            return int.TryParse(raw, out var v) ? v : fallback;
        }

        private static string NormalizeCourseName(string raw)
        {
            var text = (raw ?? string.Empty).Replace('\u00A0', ' ').Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = Regex.Replace(text, @"^\d+\s*", "");
            text = Regex.Replace(text, @"\[\d+\]\s*$", "");
            return text.Trim();
        }

        private static string NormalizeTeacher(string raw)
        {
            return (raw ?? string.Empty).Replace('\u00A0', ' ').Trim();
        }

        private static string NormalizeColor(string raw)
        {
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "#808080";

            if (Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
                return EnsureReadableOnWhite(text.ToUpperInvariant());

            var rgb = Regex.Match(text, @"rgb\s*\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*\)");
            if (rgb.Success)
            {
                var r = Math.Clamp(SafeInt(rgb.Groups["r"].Value, 128), 0, 255);
                var g = Math.Clamp(SafeInt(rgb.Groups["g"].Value, 128), 0, 255);
                var b = Math.Clamp(SafeInt(rgb.Groups["b"].Value, 128), 0, 255);
                return EnsureReadableOnWhite($"#{r:X2}{g:X2}{b:X2}");
            }

            return "#808080";
        }

        private static string EnsureReadableOnWhite(string hex)
        {
            if (!TryParseHexColor(hex, out var r, out var g, out var b))
                return "#808080";

            // Schedule cards use white text, target at least WCAG AA normal-text contrast.
            const double minContrastWithWhite = 4.5;
            if (ContrastRatioWithWhite(r, g, b) >= minContrastWithWhite)
                return $"#{r:X2}{g:X2}{b:X2}";

            // Darken step-by-step while keeping hue relationship.
            for (int i = 0; i < 12; i++)
            {
                r = (int)Math.Round(r * 0.88);
                g = (int)Math.Round(g * 0.88);
                b = (int)Math.Round(b * 0.88);
                if (ContrastRatioWithWhite(r, g, b) >= minContrastWithWhite)
                    break;
            }

            return $"#{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
        }

        private static bool TryParseHexColor(string hex, out int r, out int g, out int b)
        {
            r = g = b = 128;
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#')
                return false;

            try
            {
                r = Convert.ToInt32(hex.Substring(1, 2), 16);
                g = Convert.ToInt32(hex.Substring(3, 2), 16);
                b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double ContrastRatioWithWhite(int r, int g, int b)
        {
            var luminance = RelativeLuminance(r, g, b);
            return (1.0 + 0.05) / (luminance + 0.05);
        }

        private static double RelativeLuminance(int r, int g, int b)
        {
            var rs = SrgbToLinear(r / 255.0);
            var gs = SrgbToLinear(g / 255.0);
            var bs = SrgbToLinear(b / 255.0);
            return (0.2126 * rs) + (0.7152 * gs) + (0.0722 * bs);
        }

        private static double SrgbToLinear(double value)
        {
            if (value <= 0.03928)
                return value / 12.92;
            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
