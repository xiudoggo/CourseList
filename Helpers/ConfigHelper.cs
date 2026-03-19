using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CourseList.Helpers
{
    
    public class AppConfig
    {
        public string Theme { get; set; } = "Default";
        // 课程表显示范围：5=周一到周五，7=周一到周日
        public int ScheduleWeekRange { get; set; } = 7;
    }

    public static class ConfigHelper
    {
        private static readonly string ConfigFilePath =
            PathHelper.GetFullPath("config.json");

        public static AppConfig LoadConfig()
        {
            try
            {
                PathHelper.EnsureFolderExists();
                if (!File.Exists(ConfigFilePath))
                {
                    var defaultConfig = new AppConfig();
                    SaveConfig(defaultConfig);
                    return defaultConfig;
                }

                string json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                // 出错兜底
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                PathHelper.EnsureFolderExists();
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // 可以加日志
            }
        }
    }
}
