using CourseList.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CourseList.Helpers
{
    public static class ToDoDataHelper
    {
        internal sealed class ToDoStorage
        {
            public List<ToDoItem> Todos { get; set; } = new();
            public List<string> TagLibrary { get; set; } = new();
        }

        private static string GetToDoFilePath() => PathHelper.GetFullPath("todos.json");

        private static bool TryLoadStorage(out ToDoStorage storage)
        {
            storage = new ToDoStorage();
            try
            {
                PathHelper.EnsureFolderExists();
                string path = GetToDoFilePath();
                if (!File.Exists(path))
                    return false;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Backward compatible: old format is just `ToDoItem[]`
                    storage.Todos = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListToDoItem) ?? new List<ToDoItem>();
                    storage.TagLibrary = ExtractTagLibraryFromTodos(storage.Todos);
                    return true;
                }

                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    storage = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ToDoStorage) ?? new ToDoStorage();
                    storage.Todos ??= new List<ToDoItem>();
                    storage.TagLibrary ??= new List<string>();
                    if (storage.TagLibrary.Count == 0 && storage.Todos.Count > 0)
                        storage.TagLibrary = ExtractTagLibraryFromTodos(storage.Todos);
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static List<string> ExtractTagLibraryFromTodos(List<ToDoItem> todos)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in todos)
            {
                if (t?.Tags == null)
                    continue;
                foreach (var tag in t.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;
                    set.Add(tag.Trim());
                }
            }
            return set.ToList();
        }

        public static async Task<List<ToDoItem>> LoadToDosAsync()
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string path = GetToDoFilePath();
                if (!File.Exists(path))
                    return new List<ToDoItem>();

                if (TryLoadStorage(out var storage))
                    return storage.Todos ?? new List<ToDoItem>();

                // Fallback: old array format
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListToDoItem) ?? new List<ToDoItem>();
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

                TryLoadStorage(out var storage);
                storage.Todos = todos ?? new List<ToDoItem>();
                storage.TagLibrary ??= new List<string>();

                string json = JsonSerializer.Serialize(storage, AppJsonSerializerContext.Default.ToDoStorage);
                await File.WriteAllTextAsync(path, json);
            }
            catch
            {
                // ignore
            }
        }

        public static async Task<List<string>> LoadTagLibraryAsync()
        {
            try
            {
                if (TryLoadStorage(out var storage))
                    return storage.TagLibrary ?? new List<string>();
            }
            catch
            {
                // ignore
            }

            return new List<string>();
        }

        public static async Task SaveTagLibraryAsync(IEnumerable<string> tags)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string path = GetToDoFilePath();

                TryLoadStorage(out var storage);
                storage.TagLibrary = tags?
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                string json = JsonSerializer.Serialize(storage, AppJsonSerializerContext.Default.ToDoStorage);
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
