using CommunityToolkit.WinUI.Notifications;
using System;

namespace CourseList.Helpers
{
    public static class SystemNotificationHelper
    {
        public static void Show(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch
            {
                // Fallback: ignore if system notifications unavailable.
            }
        }
    }
}
