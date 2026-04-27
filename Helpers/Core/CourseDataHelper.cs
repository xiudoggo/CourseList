using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;


namespace CourseList.Helpers
{
    public static class CourseDataHelper
    {
        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, List<Course>> _coursesCacheBySchemeId = new Dictionary<string, List<Course>>(StringComparer.Ordinal);

        public static void InvalidateCache(string? schemeId = null)
        {
            lock (_cacheLock)
            {
                if (string.IsNullOrEmpty(schemeId))
                {
                    _coursesCacheBySchemeId.Clear();
                    return;
                }

                _coursesCacheBySchemeId.Remove(schemeId);
            }
        }

        private static string GetCourseFilePath()
        {
            SchemeHelper.EnsureMigrated();
            var currentId = SchemeHelper.GetCurrentSchemeId();
            return SchemeHelper.GetSchemeCoursesPath(currentId);
        }

        // 防止多个异步保存同时写入导致文件内容被“旧数据”覆盖
        private static readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);

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
                var currentSchemeId = SchemeHelper.GetCurrentSchemeId() ?? string.Empty;
                if (!string.IsNullOrEmpty(currentSchemeId))
                {
                    lock (_cacheLock)
                    {
                        if (_coursesCacheBySchemeId.TryGetValue(currentSchemeId, out var cached) && cached != null)
                        {
                            // 返回快照，避免调用方修改缓存引用
                            return cached.ToList();
                        }
                    }
                }

                var courseFilePath = GetCourseFilePath();
                PathHelper.EnsureFolderExists();
                var schemeFolder = Path.GetDirectoryName(courseFilePath);
                if (!string.IsNullOrEmpty(schemeFolder) && !Directory.Exists(schemeFolder))
                    Directory.CreateDirectory(schemeFolder);
                if (!File.Exists(courseFilePath))
                {
                    var empty = new List<Course>();
                    if (!string.IsNullOrEmpty(currentSchemeId))
                    {
                        lock (_cacheLock)
                        {
                            _coursesCacheBySchemeId[currentSchemeId] = empty;
                        }
                    }
                    return new List<Course>();
                }

                string json = await File.ReadAllTextAsync(courseFilePath);
                var courses = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListCourse)
                              ?? new List<Course>();
                NormalizeCourseWeeks(courses);

                if (!string.IsNullOrEmpty(currentSchemeId))
                {
                    // 缓存规范化后的快照
                    lock (_cacheLock)
                    {
                        _coursesCacheBySchemeId[currentSchemeId] = courses.ToList();
                    }
                }

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

                    // 先同步更新内存缓存（即刻让 UI 读到最新数据），落盘由去抖任务完成
                    if (!string.IsNullOrEmpty(_pendingSchemeId))
                    {
                        lock (_cacheLock)
                        {
                            _coursesCacheBySchemeId[_pendingSchemeId] = snapshot.ToList();
                        }
                    }

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

                            string json = JsonSerializer.Serialize(pending, AppJsonSerializerContext.Default.ListCourse);

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
                var json = JsonSerializer.Serialize(snapshot, AppJsonSerializerContext.Default.ListCourse);

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

                var cacheKey = string.IsNullOrWhiteSpace(schemeId) ? SchemeHelper.GetCurrentSchemeId() : schemeId;
                if (!string.IsNullOrEmpty(cacheKey))
                {
                    lock (_cacheLock)
                    {
                        _coursesCacheBySchemeId[cacheKey] = snapshot.ToList();
                    }
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