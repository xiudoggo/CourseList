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
        private static readonly string CourseFilePath =
            PathHelper.GetFullPath("courses.json");

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
        private const int DebounceMs = 120;

        public static async Task<List<Course>> LoadCoursesAsync()
        {
            try
            {
                PathHelper.EnsureFolderExists();
                if (!File.Exists(CourseFilePath))
                {
                    // 文件不存在，返回空列表
                    return new List<Course>();
                }

                string json = await File.ReadAllTextAsync(CourseFilePath);

                return JsonSerializer.Deserialize<List<Course>>(json)
                       ?? new List<Course>();
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
                // 保存时先拍一个快照，避免调用方在序列化期间修改列表造成不一致
                var snapshot = courses.ToList();

                // 去抖合并：取消上一次待写任务，使用最新 snapshot
                lock (_debounceLock)
                {
                    _pendingSnapshot = snapshot;
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

                            string json = JsonSerializer.Serialize(pending, _saveOptions);

                            await _saveSemaphore.WaitAsync();
                            try
                            {
                                await File.WriteAllTextAsync(CourseFilePath, json);
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
    }
}