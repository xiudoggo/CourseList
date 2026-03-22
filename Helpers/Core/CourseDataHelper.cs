using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;


namespace CourseList.Helpers
{
    public static class CourseDataHelper
    {
        private static string GetCourseFilePath()
        {
            SchemeHelper.EnsureMigrated();
            var currentId = SchemeHelper.GetCurrentSchemeId();
            return SchemeHelper.GetSchemeCoursesPath(currentId);
        }

        // 防止多个异步保存同时写入导致文件内容被“旧数据”覆盖
        private static readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);

        private static readonly JsonSerializerOptions _saveOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 去抖：把短时间内多次 Save 合并为一次真实落盘
        private static readonly object _debounceLock = new object();
        private static CancellationTokenSource? _debounceCts;
        private static List<Course>? _pendingSnapshot;
        private static string? _pendingSchemeId;
        private const int DebounceMs = 120;

        public static async Task<List<Course>> LoadCoursesAsync()
        {
            try
            {
                var courseFilePath = GetCourseFilePath();
                PathHelper.EnsureFolderExists();
                var schemeFolder = Path.GetDirectoryName(courseFilePath);
                if (!string.IsNullOrEmpty(schemeFolder) && !Directory.Exists(schemeFolder))
                    Directory.CreateDirectory(schemeFolder);
                if (!File.Exists(courseFilePath))
                {
                    return new List<Course>();
                }

                string json = await File.ReadAllTextAsync(courseFilePath);
                var courses = JsonSerializer.Deserialize<List<Course>>(json)
                              ?? new List<Course>();
                NormalizeCourseWeeks(courses);
                return courses;
            }
            catch
            {
                // 出错兜底
                return new List<Course>();
            }
        }

        public static Task SaveCoursesAsync(List<Course> courses)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                NormalizeCourseWeeks(courses);
                // 保存时先拍一个快照，避免调用方在序列化期间修改列表造成不一致
                var snapshot = courses.ToList();

                // 去抖合并：取消上一次待写任务，使用最新 snapshot，记录当前方案 ID
                lock (_debounceLock)
                {
                    _pendingSnapshot = snapshot;
                    _pendingSchemeId = SchemeHelper.GetCurrentSchemeId();
                    _debounceCts?.Cancel();
                    _debounceCts = new CancellationTokenSource();
                    var token = _debounceCts.Token;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(DebounceMs, token);
                            List<Course>? pending;
                            lock (_debounceLock)
                            {
                                pending = _pendingSnapshot;
                            }
                            if (pending == null)
                                return;

                            string? schemeId;
                            lock (_debounceLock)
                            {
                                schemeId = _pendingSchemeId;
                            }

                            string json = JsonSerializer.Serialize(pending, _saveOptions);

                            await _saveSemaphore.WaitAsync();
                            try
                            {
                                var path = string.IsNullOrEmpty(schemeId)
                                    ? GetCourseFilePath()
                                    : SchemeHelper.GetSchemeCoursesPath(schemeId);
                                var dir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                    Directory.CreateDirectory(dir);
                                await File.WriteAllTextAsync(path, json);
                            }
                            finally
                            {
                                _saveSemaphore.Release();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // 被新一轮保存取消了，忽略
                        }
                    });
                }
            }
            catch
            {
                // 可以加日志
            }

            // 真正落盘发生在后台去抖任务中
            return Task.CompletedTask;
        }

        public static async Task SaveCoursesImmediateAsync(List<Course> courses, string? schemeId = null)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                NormalizeCourseWeeks(courses);
                var snapshot = (courses ?? new List<Course>()).ToList();
                var json = JsonSerializer.Serialize(snapshot, _saveOptions);

                var path = string.IsNullOrWhiteSpace(schemeId)
                    ? GetCourseFilePath()
                    : SchemeHelper.GetSchemeCoursesPath(schemeId);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await _saveSemaphore.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(path, json);
                }
                finally
                {
                    _saveSemaphore.Release();
                }
            }
            catch
            {
                // ignored
            }
        }

        private static void NormalizeCourseWeeks(List<Course> courses)
        {
            if (courses == null || courses.Count == 0)
                return;

            int totalWeeks = ConfigHelper.LoadConfig().SemesterTotalWeeks;
            if (totalWeeks < 1)
                totalWeeks = 20;

            foreach (var course in courses)
            {
                if (course.FromWeek <= 0)
                    course.FromWeek = 1;
                if (course.ToWeek <= 0)
                    course.ToWeek = totalWeeks;

                course.FromWeek = Math.Clamp(course.FromWeek, 1, totalWeeks);
                course.ToWeek = Math.Clamp(course.ToWeek, 1, totalWeeks);
                if (course.FromWeek > course.ToWeek)
                    (course.FromWeek, course.ToWeek) = (course.ToWeek, course.FromWeek);
            }
        }
    }
}