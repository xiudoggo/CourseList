using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;


namespace CourseList.Helpers
{
    public static class CourseDataHelper
    {
        private static readonly string CourseFilePath =
            PathHelper.GetFullPath("courses.json");

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

        public static async Task SaveCoursesAsync(List<Course> courses)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string json = JsonSerializer.Serialize(courses, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(CourseFilePath, json);
            }
            catch
            {
                // 可以加日志
            }
        }
    }
}