using System.IO;

namespace CourseList.Helpers
{
    public static class PathHelper
    {
        // ⭐ 统一管理数据路径
        public static readonly string BaseFolder = @"E:\VS2022\Projects\CourseList";

        public static string GetFullPath(string fileName)
        {
            return Path.Combine(BaseFolder, fileName);
        }

        public static void EnsureFolderExists()
        {
            if (!Directory.Exists(BaseFolder))
            {
                Directory.CreateDirectory(BaseFolder);
            }
        }
    }
}