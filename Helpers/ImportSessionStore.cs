using System;
using System.IO;

namespace CourseList.Helpers
{
    public static class ImportSessionStore
    {
        private static readonly string _webViewUserDataFolder =
            Path.Combine(PathHelper.BaseFolder, ".webview2", "jwxt");

        public static string WebViewUserDataFolder => _webViewUserDataFolder;

        public static Uri DefaultUrl { get; } = new Uri("https://jw.jnu.edu.cn/new/index.html");

        public static Uri? LastVisitedUrl { get; set; }
    }
}
