using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CourseList.Helpers
{
    /// <summary>
    /// 教务 WebView2 会话：用户数据目录、默认/上次访问 URL。
    /// </summary>
    public static class ImportSessionStore
    {
        private static readonly string _webViewUserDataFolder =
            Path.Combine(PathHelper.BaseFolder, ".webview2", "jwxt");

        public static string WebViewUserDataFolder => _webViewUserDataFolder;

        public static Uri DefaultUrl { get; } = new Uri("https://jw.jnu.edu.cn/new/index.html");

        public static Uri? LastVisitedUrl { get; set; }
    }

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

    /// <summary>
    /// 从教务页面脚本执行结果解析为 <see cref="Course"/> 列表。
    /// </summary>
    public static class CourseImportParser
    {
        public static CourseImportParseResult ParseFromExecuteScriptResult(string executeScriptRawResult, int totalWeeks)
        {
            try
            {
                var jsonPayload = JsonSerializer.Deserialize(executeScriptRawResult, AppJsonSerializerContext.Default.String) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(jsonPayload))
                {
                    return new CourseImportParseResult
                    {
                        Success = false,
                        ErrorMessage = "脚本返回为空，未检测到课程表。"
                    };
                }

                var payload = JsonSerializer.Deserialize(jsonPayload, AppJsonSerializerContext.Default.ImportPayload);
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
                var normalizedColor = NormalizeColor(row.color, $"{normalizedName}|{normalizedTeacher}");
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

        private static string NormalizeColor(string raw, string seed)
        {
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return PickPaletteColor(seed);

            if (Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
                return EnsureNiceCourseColor(text.ToUpperInvariant());

            var rgb = Regex.Match(text, @"rgb\s*\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*\)");
            if (rgb.Success)
            {
                var r = Math.Clamp(SafeInt(rgb.Groups["r"].Value, 128), 0, 255);
                var g = Math.Clamp(SafeInt(rgb.Groups["g"].Value, 128), 0, 255);
                var b = Math.Clamp(SafeInt(rgb.Groups["b"].Value, 128), 0, 255);
                return EnsureNiceCourseColor($"#{r:X2}{g:X2}{b:X2}");
            }

            return PickPaletteColor(string.IsNullOrWhiteSpace(seed) ? text : seed);
        }

        private static string EnsureNiceCourseColor(string hex)
        {
            if (!TryParseHexColor(hex, out var r, out var g, out var b))
                return "#808080";

            // 教务导入色往往偏暗/偏灰，这里做“适度提亮 + 提饱和”并限制亮度范围：
            // - 不要太暗：避免整块看起来发黑
            // - 不要太亮：避免完全变成浅色导致观感发白（文字颜色会在 UI 侧自动黑/白切换）
            RgbToHsl(r, g, b, out var h, out var s, out var l);

            s = Math.Clamp(Math.Max(s, 0.58), 0.0, 0.92);
            l = Math.Clamp(l, 0.48, 0.74);

            HslToRgb(h, s, l, out r, out g, out b);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string PickPaletteColor(string seed)
        {
            // 更丰富、偏明亮的调色板（背景色）；最终文字颜色由 UI 自适配（黑/白）。
            string[] palette =
            {
                "#4E79A7", "#59A14F", "#9C755F", "#F28E2B", "#EDC949",
                "#76B7B2", "#E15759", "#AF7AA1", "#FF9DA7", "#B07AA1",
                "#5DA5DA", "#60BD68", "#F17CB0", "#B2912F", "#B276B2",
                "#DECF3F", "#F15854", "#4D4D4D"
            };
            int idx = StableHash(seed) % palette.Length;
            if (idx < 0) idx += palette.Length;
            return EnsureNiceCourseColor(palette[idx]);
        }

        private static int StableHash(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            unchecked
            {
                int hash = 23;
                foreach (char c in text)
                    hash = (hash * 31) + c;
                return hash;
            }
        }

        private static void RgbToHsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));

            l = (max + min) / 2.0;
            if (Math.Abs(max - min) < 1e-9)
            {
                h = 0;
                s = 0;
                return;
            }

            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (Math.Abs(max - rd) < 1e-9)
                h = (gd - bd) / d + (gd < bd ? 6.0 : 0.0);
            else if (Math.Abs(max - gd) < 1e-9)
                h = (bd - rd) / d + 2.0;
            else
                h = (rd - gd) / d + 4.0;

            h /= 6.0;
        }

        private static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
        {
            double rd, gd, bd;

            if (s <= 1e-9)
            {
                rd = gd = bd = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : (l + s - l * s);
                double p = 2.0 * l - q;
                rd = HueToRgb(p, q, h + 1.0 / 3.0);
                gd = HueToRgb(p, q, h);
                bd = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            r = (int)Math.Round(Math.Clamp(rd, 0, 1) * 255);
            g = (int)Math.Round(Math.Clamp(gd, 0, 1) * 255);
            b = (int)Math.Round(Math.Clamp(bd, 0, 1) * 255);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
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
    }
}
