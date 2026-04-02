using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace CourseList.Helpers
{
    public static class ToDoDataHelper
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string GetToDoFilePath() => PathHelper.GetFullPath("todos.json");

        public static async Task<List<ToDoItem>> LoadToDosAsync()
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string path = GetToDoFilePath();
                if (!File.Exists(path))
                    return new List<ToDoItem>();

                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<List<ToDoItem>>(json) ?? new List<ToDoItem>();
            }
            catch
            {
                return new List<ToDoItem>();
            }
        }

        public static async Task SaveToDosAsync(List<ToDoItem> todos)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string path = GetToDoFilePath();
                string json = JsonSerializer.Serialize(todos ?? new List<ToDoItem>(), _jsonOptions);
                await File.WriteAllTextAsync(path, json);
            }
            catch
            {
                // ignore
            }
        }

        public static int GetNextId(List<ToDoItem> todos)
        {
            var real = todos?.Where(t => t is { IsDragPlaceholder: false }).ToList() ?? new List<ToDoItem>();
            if (real.Count == 0)
                return 1;
            return Math.Max(1, real.Max(t => t.Id) + 1);
        }
    }
}
