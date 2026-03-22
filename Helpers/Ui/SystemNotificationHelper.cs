using CommunityToolkit.WinUI.Notifications;
using System;
using Windows.UI.Notifications;

namespace CourseList.Helpers
{
    public static class SystemNotificationHelper
    {
        /// <summary>
        /// 静默：无提示音，尽量仅出现在通知中心（不弹横幅，若系统支持）。
        /// </summary>
        public static void ShowSilent(string title, string message)
        {
            try
            {
                var content = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddAudio(new ToastAudio { Silent = true });

                var doc = content.GetToastContent().GetXml();
                var toast = new ToastNotification(doc);
                try
                {
                    toast.SuppressPopup = true;
                }
                catch
                {
                    // 部分环境可能不支持该属性
                }

                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch
            {
                // 通知不可用时忽略
            }
        }

        /// <summary>兼容旧调用：等同于静默通知。</summary>
        public static void Show(string title, string message) => ShowSilent(title, message);
    }
}
