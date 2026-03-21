using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CourseList.Helpers;

using Microsoft.UI;
using Microsoft.UI.Dispatching;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CourseList
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private Microsoft.Windows.AppLifecycle.AppInstance? _mainInstance;

        /// <summary>
        /// 获取当前活动的主窗口（避免与基类成员混淆，使用自定义名称）
        /// </summary>
        public static Window? CurrentMainWindow => ((App)Current)._window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var currentInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("main");
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
                return;
            }

            _mainInstance = mainInstance;
            _mainInstance.Activated -= MainInstance_Activated;
            _mainInstance.Activated += MainInstance_Activated;

            SchemeHelper.EnsureMigrated();
            await CourseDataHelper.LoadCoursesAsync();

            _window = new MainWindow();
            _window.Activate();

            // ⭐ 加这一段
            var config = ConfigHelper.LoadConfig();
            ThemeHelper.ApplyTheme(config.Theme);
        }

        private void MainInstance_Activated(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments e)
        {
            if (_window == null)
            {
                _window = new MainWindow();
                _window.Activate();
                return;
            }

            if (_window.DispatcherQueue != null)
            {
                _window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    EnsureWindowForeground();
                });
                return;
            }

            EnsureWindowForeground();
        }

        private void EnsureWindowForeground()
        {
            if (_window is MainWindow mainWindow)
            {
                mainWindow.BringToFrontFromActivation();
                return;
            }

            _window?.Activate();
        }



        
        
    }
}
