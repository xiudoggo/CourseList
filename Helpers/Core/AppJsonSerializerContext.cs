using CourseList.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CourseList.Helpers;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Course>))]
[JsonSerializable(typeof(List<ToDoItem>))]
[JsonSerializable(typeof(ToDoDataHelper.ToDoStorage))]
[JsonSerializable(typeof(List<WeekScheduleOverride>))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ConfigHelper.SchemeConfigData))]
[JsonSerializable(typeof(SchemeHelper.SchemeConfigSeed))]
[JsonSerializable(typeof(SchemeHelper.SchemesFileData))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ImportPayload))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

