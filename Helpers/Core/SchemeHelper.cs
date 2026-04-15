using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CourseList.Helpers
{
    /// <summary>
    /// 课表方案信息
    /// </summary>
    public class SchemeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class SchemeGlobalSettings
    {
        public string Theme { get; set; } = "Default";
        public bool MinimizeToTrayOnClose { get; set; } = false;
        public bool ClosePromptEnabled { get; set; } = true;
        public double TodoPinWindowWidthDip { get; set; } = 500;
        public double TodoPinWindowHeightDip { get; set; } = 500;
    }

    /// <summary>
    /// 课表方案管理：多方案的 CRUD、切换、路径解析
    /// </summary>
    public static class SchemeHelper
    {
        private static readonly string SchemesFilePath = PathHelper.GetFullPath("schemes.json");
        private static readonly string SchemesFolder = PathHelper.GetFullPath("schemes");
        private static readonly string LegacyConfigPath = PathHelper.GetFullPath("config.json");
        private static readonly string LegacyCoursesPath = PathHelper.GetFullPath("courses.json");
        private static bool _migrationChecked;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 首次运行或从旧版迁移：若无 schemes.json 则从 config.json + courses.json 创建默认方案
        /// </summary>
        public static void EnsureMigrated()
        {
            if (_migrationChecked)
                return;
            _migrationChecked = true;

            PathHelper.EnsureFolderExists();
            if (File.Exists(SchemesFilePath))
            {
                TryMergeLegacyGlobalIntoSchemes();
                return;
            }

            var newId = Guid.NewGuid().ToString("N");
            var folder = GetSchemeFolder(newId);
            Directory.CreateDirectory(folder);

            if (File.Exists(LegacyConfigPath))
            {
                var dstConfig = GetSchemeConfigPath(newId);
                File.Copy(LegacyConfigPath, dstConfig);
            }
            else
            {
                var cfgPath = GetSchemeConfigPath(newId);
                var defaultCfg = new
                {
                    ScheduleWeekRange = 7,
                    PeriodCount = 11,
                    SemesterStartMonday = DateTime.Today,
                    SemesterTotalWeeks = 20,
                    PeriodTimeRanges = new List<object>()
                };
                File.WriteAllText(cfgPath, JsonSerializer.Serialize(defaultCfg, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (File.Exists(LegacyCoursesPath))
            {
                var dstCourses = GetSchemeCoursesPath(newId);
                File.Copy(LegacyCoursesPath, dstCourses);
            }
            else
            {
                File.WriteAllText(GetSchemeCoursesPath(newId), "[]");
            }

            var schemes = new List<SchemeInfo> { new SchemeInfo { Id = newId, Name = "默认方案" } };
            var global = ReadLegacyGlobalSettings();
            SaveSchemes(schemes, newId, global);
        }

        /// <summary>
        /// 方案切换时触发，供各页面刷新数据
        /// </summary>
        public static event EventHandler? SchemeChanged;

        /// <summary>
        /// 加载方案列表与当前方案 ID
        /// </summary>
        public static (List<SchemeInfo> Schemes, string CurrentSchemeId) LoadSchemes()
        {
            try
            {
                EnsureMigrated();
                PathHelper.EnsureFolderExists();
                if (!File.Exists(SchemesFilePath))
                {
                    return (new List<SchemeInfo>(), string.Empty);
                }

                string json = File.ReadAllText(SchemesFilePath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var schemes = new List<SchemeInfo>();
                if (root.TryGetProperty("Schemes", out var schemesEl) && schemesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in schemesEl.EnumerateArray())
                    {
                        schemes.Add(new SchemeInfo
                        {
                            Id = item.TryGetProperty("Id", out var idEl) ? idEl.GetString() ?? "" : "",
                            Name = item.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() ?? "" : ""
                        });
                    }
                }

                var currentId = root.TryGetProperty("CurrentSchemeId", out var curEl)
                    ? curEl.GetString() ?? ""
                    : "";

                return (schemes, currentId);
            }
            catch
            {
                return (new List<SchemeInfo>(), string.Empty);
            }
        }

        /// <summary>
        /// 保存方案列表与当前方案 ID
        /// </summary>
        public static void SaveSchemes(List<SchemeInfo> schemes, string currentSchemeId)
        {
            SaveSchemes(schemes, currentSchemeId, LoadGlobalSettings());
        }

        /// <summary>
        /// 保存方案列表、当前方案 ID 及全局设置到 schemes.json
        /// </summary>
        public static void SaveSchemes(List<SchemeInfo> schemes, string currentSchemeId, SchemeGlobalSettings global)
        {
            try
            {
                EnsureMigrated();
                PathHelper.EnsureFolderExists();
                var obj = new
                {
                    Theme = global.Theme,
                    MinimizeToTrayOnClose = global.MinimizeToTrayOnClose,
                    ClosePromptEnabled = global.ClosePromptEnabled,
                    TodoPinWindowWidthDip = global.TodoPinWindowWidthDip,
                    TodoPinWindowHeightDip = global.TodoPinWindowHeightDip,
                    CurrentSchemeId = currentSchemeId,
                    Schemes = schemes
                };
                string json = JsonSerializer.Serialize(obj, _jsonOptions);
                File.WriteAllText(SchemesFilePath, json);
            }
            catch
            {
                // 可加日志
            }
        }

        /// <summary>
        /// 读取 schemes.json 中的全局设置（主题/关闭行为）
        /// </summary>
        public static SchemeGlobalSettings LoadGlobalSettings()
        {
            try
            {
                EnsureMigrated();
                if (!File.Exists(SchemesFilePath))
                    return new SchemeGlobalSettings();

                using var doc = JsonDocument.Parse(File.ReadAllText(SchemesFilePath));
                var root = doc.RootElement;
                var result = new SchemeGlobalSettings();
                if (root.TryGetProperty("Theme", out var t))
                    result.Theme = t.GetString() ?? result.Theme;
                if (root.TryGetProperty("MinimizeToTrayOnClose", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result.MinimizeToTrayOnClose = m.GetBoolean();
                if (root.TryGetProperty("ClosePromptEnabled", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result.ClosePromptEnabled = c.GetBoolean();
                if (root.TryGetProperty("TodoPinWindowWidthDip", out var w) && w.ValueKind == JsonValueKind.Number)
                    result.TodoPinWindowWidthDip = w.GetDouble();
                if (root.TryGetProperty("TodoPinWindowHeightDip", out var h) && h.ValueKind == JsonValueKind.Number)
                    result.TodoPinWindowHeightDip = h.GetDouble();
                return result;
            }
            catch
            {
                return new SchemeGlobalSettings();
            }
        }

        /// <summary>
        /// 仅保存全局设置到 schemes.json（保留现有方案列表）
        /// </summary>
        public static void SaveGlobalSettings(SchemeGlobalSettings global)
        {
            var (schemes, currentId) = LoadSchemes();
            SaveSchemes(schemes, currentId, global);
        }

        /// <summary>
        /// 获取当前方案 ID
        /// </summary>
        public static string GetCurrentSchemeId()
        {
            var (_, currentId) = LoadSchemes();
            return currentId;
        }

        /// <summary>
        /// 设置当前方案 ID 并保存
        /// </summary>
        public static void SetCurrentSchemeId(string schemeId)
        {
            var (schemes, _) = LoadSchemes();
            if (string.IsNullOrEmpty(schemeId) || !schemes.Exists(s => s.Id == schemeId))
                return;
            SaveSchemes(schemes, schemeId);
            // 方案切换后配置/课程等缓存需失效，避免页面读到旧方案数据
            try { ConfigHelper.InvalidateCache(); } catch { }
            try { CourseDataHelper.InvalidateCache(); } catch { }
            SchemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static SchemeGlobalSettings ReadLegacyGlobalSettings()
        {
            var result = new SchemeGlobalSettings();
            if (!File.Exists(LegacyConfigPath))
                return result;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(LegacyConfigPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("Theme", out var t))
                    result.Theme = t.GetString() ?? result.Theme;
                if (root.TryGetProperty("MinimizeToTrayOnClose", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result.MinimizeToTrayOnClose = m.GetBoolean();
                if (root.TryGetProperty("ClosePromptEnabled", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result.ClosePromptEnabled = c.GetBoolean();
                if (root.TryGetProperty("TodoPinWindowWidthDip", out var w) && w.ValueKind == JsonValueKind.Number)
                    result.TodoPinWindowWidthDip = w.GetDouble();
                if (root.TryGetProperty("TodoPinWindowHeightDip", out var h) && h.ValueKind == JsonValueKind.Number)
                    result.TodoPinWindowHeightDip = h.GetDouble();
            }
            catch
            {
            }
            return result;
        }

        private static void TryMergeLegacyGlobalIntoSchemes()
        {
            if (!File.Exists(LegacyConfigPath) || !File.Exists(SchemesFilePath))
                return;
            try
            {
                var global = LoadGlobalSettings();
                bool hasTheme = false;
                bool hasMinimize = false;
                bool hasPrompt = false;
                bool hasPinWidth = false;
                bool hasPinHeight = false;
                using (var doc = JsonDocument.Parse(File.ReadAllText(SchemesFilePath)))
                {
                    var root = doc.RootElement;
                    hasTheme = root.TryGetProperty("Theme", out _);
                    hasMinimize = root.TryGetProperty("MinimizeToTrayOnClose", out _);
                    hasPrompt = root.TryGetProperty("ClosePromptEnabled", out _);
                    hasPinWidth = root.TryGetProperty("TodoPinWindowWidthDip", out _);
                    hasPinHeight = root.TryGetProperty("TodoPinWindowHeightDip", out _);
                }
                if (hasTheme && hasMinimize && hasPrompt && hasPinWidth && hasPinHeight)
                    return;

                var legacy = ReadLegacyGlobalSettings();
                if (!hasTheme) global.Theme = legacy.Theme;
                if (!hasMinimize) global.MinimizeToTrayOnClose = legacy.MinimizeToTrayOnClose;
                if (!hasPrompt) global.ClosePromptEnabled = legacy.ClosePromptEnabled;
                if (!hasPinWidth) global.TodoPinWindowWidthDip = legacy.TodoPinWindowWidthDip;
                if (!hasPinHeight) global.TodoPinWindowHeightDip = legacy.TodoPinWindowHeightDip;
                SaveGlobalSettings(global);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 获取方案文件夹路径（不含文件名）
        /// </summary>
        public static string GetSchemeFolder(string schemeId)
        {
            if (string.IsNullOrEmpty(schemeId))
                return Path.Combine(SchemesFolder, "default");
            return Path.Combine(SchemesFolder, schemeId);
        }

        /// <summary>
        /// 获取方案 config.json 路径
        /// </summary>
        public static string GetSchemeConfigPath(string schemeId)
        {
            return Path.Combine(GetSchemeFolder(schemeId), "config.json");
        }

        /// <summary>
        /// 获取方案 courses.json 路径
        /// </summary>
        public static string GetSchemeCoursesPath(string schemeId)
        {
            return Path.Combine(GetSchemeFolder(schemeId), "courses.json");
        }

        /// <summary>
        /// 按周调课覆盖（week-overrides.json）
        /// </summary>
        public static string GetSchemeWeekOverridesPath(string schemeId)
        {
            return Path.Combine(GetSchemeFolder(schemeId), "week-overrides.json");
        }

        /// <summary>
        /// 新建方案。copyFromId 不为空时从该方案复制配置与课程
        /// </summary>
        /// <returns>新方案 ID，失败返回 null</returns>
        public static string? CreateScheme(string name, string? copyFromId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var (schemes, currentId) = LoadSchemes();
            var newId = Guid.NewGuid().ToString("N");
            var folder = GetSchemeFolder(newId);
            Directory.CreateDirectory(folder);

            if (!string.IsNullOrEmpty(copyFromId) && schemes.Exists(s => s.Id == copyFromId))
            {
                var srcConfig = GetSchemeConfigPath(copyFromId);
                var srcCourses = GetSchemeCoursesPath(copyFromId);
                var srcOverrides = GetSchemeWeekOverridesPath(copyFromId);
                var dstConfig = GetSchemeConfigPath(newId);
                var dstCourses = GetSchemeCoursesPath(newId);
                var dstOverrides = GetSchemeWeekOverridesPath(newId);
                if (File.Exists(srcConfig))
                    File.Copy(srcConfig, dstConfig);
                if (File.Exists(srcCourses))
                    File.Copy(srcCourses, dstCourses);
                if (File.Exists(srcOverrides))
                    File.Copy(srcOverrides, dstOverrides);
            }
            else
            {
                var cfgPath = GetSchemeConfigPath(newId);
                var defaultSchemeConfig = new
                {
                    ScheduleWeekRange = 7,
                    PeriodCount = 11,
                    SemesterStartMonday = DateTime.Today,
                    SemesterTotalWeeks = 20,
                    PeriodTimeRanges = new List<object>()
                };
                File.WriteAllText(cfgPath, JsonSerializer.Serialize(defaultSchemeConfig, new JsonSerializerOptions { WriteIndented = true }));
            }

            schemes.Add(new SchemeInfo { Id = newId, Name = name.Trim() });
            var targetCurrent = string.IsNullOrEmpty(currentId) && schemes.Count == 1 ? newId : currentId;
            SaveSchemes(schemes, targetCurrent);
            return newId;
        }

        /// <summary>
        /// 删除方案。不能删除当前方案，且至少保留一个方案
        /// </summary>
        public static bool DeleteScheme(string schemeId)
        {
            var (schemes, currentId) = LoadSchemes();
            if (schemes.Count <= 1)
                return false;
            if (schemeId == currentId)
                return false;

            var idx = schemes.FindIndex(s => s.Id == schemeId);
            if (idx < 0)
                return false;

            schemes.RemoveAt(idx);
            SaveSchemes(schemes, currentId);

            var folder = GetSchemeFolder(schemeId);
            if (Directory.Exists(folder))
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {
                    // 忽略删除失败
                }
            }
            return true;
        }

        /// <summary>
        /// 重命名方案
        /// </summary>
        public static bool RenameScheme(string schemeId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            var (schemes, currentId) = LoadSchemes();
            var scheme = schemes.Find(s => s.Id == schemeId);
            if (scheme == null)
                return false;

            scheme.Name = newName.Trim();
            SaveSchemes(schemes, currentId);
            return true;
        }
    }
}
