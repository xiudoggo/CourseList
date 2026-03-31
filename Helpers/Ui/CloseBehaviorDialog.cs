using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CourseList.Helpers.Ui
{
    internal sealed class CloseBehaviorDialogResult
    {
        public bool Confirmed { get; init; }
        public bool MinimizeToTray { get; init; }
        public bool DontRemindAgain { get; init; }
    }

    internal static class CloseBehaviorDialog
    {
        internal static async Task<CloseBehaviorDialogResult> ShowAsync(
            XamlRoot? xamlRoot,
            ElementTheme requestedTheme,
            bool initialMinimizeToTray)
        {
            var minimizeRadio = new RadioButton
            {
                Content = "最小化到系统托盘",
                IsChecked = initialMinimizeToTray,
                GroupName = "CloseBehavior"
            };

            var directRadio = new RadioButton
            {
                Content = "直接关闭",
                IsChecked = !initialMinimizeToTray,
                GroupName = "CloseBehavior"
            };

            var dontRemindCheck = new CheckBox
            {
                Content = "不再提示",
                IsChecked = false
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock
            {
                Text = "请选择关闭方式：",
            });
            stack.Children.Add(minimizeRadio);
            stack.Children.Add(directRadio);
            stack.Children.Add(dontRemindCheck);

            var dialog = new ContentDialog
            {
                Title = "点击关闭按钮后：",
                Content = stack,
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                XamlRoot = xamlRoot,
                RequestedTheme = requestedTheme
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            if (result != ContentDialogResult.Primary)
            {
                return new CloseBehaviorDialogResult
                {
                    Confirmed = false
                };
            }

            return new CloseBehaviorDialogResult
            {
                Confirmed = true,
                MinimizeToTray = minimizeRadio.IsChecked == true,
                DontRemindAgain = dontRemindCheck.IsChecked == true
            };
        }
    }
}
