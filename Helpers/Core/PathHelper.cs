using System.IO;
using Windows.Storage;

namespace CourseList.Helpers
{
    public static class PathHelper
    {
        // 统一管理应用数据路径（AppData\Local\Packages\<Package>\LocalState 或对应本地应用目录）
        public static string BaseFolder => ApplicationData.Current.LocalFolder.Path;

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