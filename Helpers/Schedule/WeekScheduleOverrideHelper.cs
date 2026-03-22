using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using CourseList.Models;

namespace CourseList.Helpers
{
    public static class WeekScheduleOverrideHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static async Task<List<WeekScheduleOverride>> LoadAsync(string? schemeId = null)
        {
            schemeId ??= SchemeHelper.GetCurrentSchemeId();
            if (string.IsNullOrEmpty(schemeId))
                return new List<WeekScheduleOverride>();

            var path = SchemeHelper.GetSchemeWeekOverridesPath(schemeId);
            if (!File.Exists(path))
                return new List<WeekScheduleOverride>();

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var list = JsonSerializer.Deserialize<List<WeekScheduleOverride>>(json, JsonOptions);
                return list ?? new List<WeekScheduleOverride>();
            }
            catch
            {
                return new List<WeekScheduleOverride>();
            }
        }

        public static List<WeekScheduleOverride> Load(string? schemeId = null)
        {
            return LoadAsync(schemeId).GetAwaiter().GetResult();
        }

        public static async Task SaveAsync(List<WeekScheduleOverride> overrides, string? schemeId = null)
        {
            schemeId ??= SchemeHelper.GetCurrentSchemeId();
            if (string.IsNullOrEmpty(schemeId))
                return;

            var folder = SchemeHelper.GetSchemeFolder(schemeId);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var path = SchemeHelper.GetSchemeWeekOverridesPath(schemeId);
            var json = JsonSerializer.Serialize(overrides ?? new List<WeekScheduleOverride>(), JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }

        public static void RemoveAllForCourse(List<WeekScheduleOverride> overrides, int courseId)
        {
            overrides.RemoveAll(o => o.CourseId == courseId);
        }

        public static void Upsert(List<WeekScheduleOverride> overrides, WeekScheduleOverride item)
        {
            overrides.RemoveAll(o => o.CourseId == item.CourseId && o.WeekIndex == item.WeekIndex);
            overrides.Add(item);
        }

        public static WeekScheduleOverride? Find(List<WeekScheduleOverride> overrides, int courseId, int weekIndex)
        {
            return overrides.FirstOrDefault(o => o.CourseId == courseId && o.WeekIndex == weekIndex);
        }
    }
}
