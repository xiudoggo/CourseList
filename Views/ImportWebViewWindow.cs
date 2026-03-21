using CourseList.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace CourseList.Views
{
    public sealed class ImportWebViewWindow : Window
    {
        private static ImportWebViewWindow? _instance;

        private readonly Grid _rootGrid;
        private readonly Grid _layoutGrid;
        private readonly Grid _topBarGrid;
        private readonly WebView2 _importWebView;
        private readonly TextBox _addressBar;
        private readonly Button _goButton;
        private readonly Button _downloadImportButton;
        private bool _webInited;

        private const string TableProbeScript = @"
(() => {
  const table = document.querySelector('table.wut_table');
  if (!table) return JSON.stringify({ ok: false, error: 'NOT_FOUND_TABLE', count: 0, rows: [] });
  const cells = Array.from(table.querySelectorAll('td[data-role=""item""]'));
  const rows = [];
  for (const cell of cells) {
    const item = cell.querySelector('.mtt_arrange_item');
    if (!item) continue;
    const name = (item.querySelector('.mtt_item_kcmc')?.textContent || '').replace(/\u00a0/g, ' ').trim();
    const teacher = (item.querySelector('.mtt_item_jxbmc')?.textContent || '').replace(/\u00a0/g, ' ').trim();
    const roomText = (item.querySelector('.mtt_item_room')?.textContent || '').replace(/\u00a0/g, ' ').trim();
    const style = item.getAttribute('style') || '';
    const m = style.match(/background-color\s*:\s*([^;]+)/i);
    const color = m ? m[1].trim() : '';
    rows.push({
      day: parseInt(cell.getAttribute('data-week') || '0', 10),
      begin: parseInt(cell.getAttribute('data-begin-unit') || '0', 10),
      end: parseInt(cell.getAttribute('data-end-unit') || '0', 10),
      name, teacher, roomText, color
    });
  }
  return JSON.stringify({ ok: true, count: rows.length, rows });
})();";

        public static void OpenOrActivate()
        {
            if (_instance != null)
            {
                _instance.Activate();
                return;
            }

            _instance = new ImportWebViewWindow();
            _instance.Activate();
            _instance.TryMatchMainWindowSize();
        }

        private ImportWebViewWindow()
        {
            Title = "教务系统导入";

            _rootGrid = new Grid { MinWidth = 980, MinHeight = 620 };
            _layoutGrid = new Grid();
            _layoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _topBarGrid = new Grid
            {
                Margin = new Thickness(12, 10, 12, 8)
            };
            _topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _topBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _addressBar = new TextBox
            {
                PlaceholderText = "输入网址",
                Text = (ImportSessionStore.LastVisitedUrl ?? ImportSessionStore.DefaultUrl).ToString(),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _addressBar.KeyDown += AddressBar_KeyDown;
            Grid.SetColumn(_addressBar, 0);

            _goButton = new Button
            {
                Content = "前往",
                MinWidth = 72
            };
            _goButton.Click += GoButton_Click;
            Grid.SetColumn(_goButton, 1);

            _topBarGrid.Children.Add(_addressBar);
            _topBarGrid.Children.Add(_goButton);

            _importWebView = new WebView2();
            Grid.SetRow(_importWebView, 1);
            _downloadImportButton = new Button
            {
                Content = "下载导入",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 18, 18),
                Padding = new Thickness(14, 8, 14, 8)
            };
            _downloadImportButton.Click += DownloadImportButton_Click;

            _layoutGrid.Children.Add(_topBarGrid);
            _layoutGrid.Children.Add(_importWebView);
            _rootGrid.Children.Add(_layoutGrid);
            _rootGrid.Children.Add(_downloadImportButton);
            Content = _rootGrid;

            if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var styleObj) && styleObj is Style accentStyle)
            {
                _downloadImportButton.Style = accentStyle;
            }

            Closed += ImportWebViewWindow_Closed;
            Activated += ImportWebViewWindow_Activated;
        }

        private async void ImportWebViewWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_webInited)
                return;
            _webInited = true;

            try
            {
                await _importWebView.EnsureCoreWebView2Async();
                _importWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _importWebView.NavigationCompleted += ImportWebView_NavigationCompleted;
                _importWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                NavigateToAddressBarUrl();
            }
            catch (Exception ex)
            {
                SystemNotificationHelper.Show("教务导入", $"WebView2 初始化失败：{ex.Message}");
            }
        }

        private void ImportWebViewWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_importWebView?.Source != null)
            {
                ImportSessionStore.LastVisitedUrl = new Uri(_importWebView.Source.ToString());
            }
            _instance = null;
        }

        private void ImportWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (sender?.Source != null)
            {
                ImportSessionStore.LastVisitedUrl = new Uri(sender.Source.ToString());
                _addressBar.Text = sender.Source.ToString();
            }
        }

        private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            // 教务系统常通过 window.open 打开“新页面”（例如课表图形页）。
            // 强制在当前 WebView 内接管，保证悬浮“下载导入”按钮始终可见。
            args.Handled = true;
            var targetUri = args.Uri;
            if (!string.IsNullOrWhiteSpace(targetUri))
            {
                sender.Navigate(targetUri);
            }
        }

        private void AddressBar_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                NavigateToAddressBarUrl();
                e.Handled = true;
            }
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAddressBarUrl();
        }

        private void NavigateToAddressBarUrl()
        {
            if (_importWebView?.CoreWebView2 == null)
                return;

            var raw = (_addressBar.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                raw = ImportSessionStore.DefaultUrl.ToString();

            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "https://" + raw;
            }

            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                _importWebView.CoreWebView2.Navigate(uri.ToString());
            }
        }

        private void TryMatchMainWindowSize()
        {
            try
            {
                if (App.CurrentMainWindow == null)
                    return;

                var mainAppWindow = App.CurrentMainWindow.AppWindow;
                var thisAppWindow = this.AppWindow;
                var b = mainAppWindow.Size;
                var p = mainAppWindow.Position;
                thisAppWindow.MoveAndResize(new RectInt32(p.X, p.Y, b.Width, b.Height));
            }
            catch
            {
                // keep default window size
            }
        }

        private async void DownloadImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_importWebView?.CoreWebView2 == null)
            {
                await ShowInlineMessageAsync("页面未就绪，请稍后重试。");
                return;
            }

            try
            {
                var raw = await _importWebView.ExecuteScriptAsync(TableProbeScript);
                var config = ConfigHelper.LoadConfig();
                var parse = CourseImportParser.ParseFromExecuteScriptResult(raw, config.SemesterTotalWeeks);
                if (!parse.Success)
                {
                    await ShowInlineMessageAsync(parse.ErrorMessage == "NOT_FOUND_TABLE" ? "当前页面未检测到课程表，请先切换到课表图形化页面。" : parse.ErrorMessage);
                    return;
                }

                if (parse.Courses.Count == 0)
                {
                    await ShowInlineMessageAsync("检测到课程表，但未解析到可导入课程。");
                    return;
                }

                var action = await ShowImportTargetChoiceAsync(parse.Courses.Count, parse.RawItemCount);
                if (action == ImportTargetChoice.Cancel)
                    return;

                if (action == ImportTargetChoice.OverwriteCurrent)
                {
                    var currentId = SchemeHelper.GetCurrentSchemeId();
                    await CourseDataHelper.SaveCoursesImmediateAsync(parse.Courses, currentId);
                    SchemeHelper.SetCurrentSchemeId(currentId);
                    SystemNotificationHelper.Show("教务导入", $"导入成功：已覆盖当前方案，共 {parse.Courses.Count} 门课。");
                    Close();
                }
                else
                {
                    var newSchemeName = GetNextImportSchemeName();
                    var newId = SchemeHelper.CreateScheme(newSchemeName, copyFromId: null);
                    if (string.IsNullOrWhiteSpace(newId))
                    {
                        await ShowInlineMessageAsync("创建导入方案失败，请重试。");
                        return;
                    }

                    SchemeHelper.SetCurrentSchemeId(newId);
                    await CourseDataHelper.SaveCoursesImmediateAsync(parse.Courses, newId);
                    SchemeHelper.SetCurrentSchemeId(newId);
                    SystemNotificationHelper.Show("教务导入", $"导入成功：已创建「{newSchemeName}」，共 {parse.Courses.Count} 门课。");
                    Close();
                }
            }
            catch (Exception ex)
            {
                await ShowInlineMessageAsync($"导入失败：{ex.Message}");
            }
        }

        private enum ImportTargetChoice
        {
            Cancel,
            OverwriteCurrent,
            CreateNewScheme
        }

        private async Task<ImportTargetChoice> ShowImportTargetChoiceAsync(int parsedCount, int rawCount)
        {
            var dialog = new ContentDialog
            {
                Title = "选择导入方式",
                Content = $"识别到 {rawCount} 条原始课程格，解析为 {parsedCount} 门课程。\n请选择导入目标：",
                PrimaryButtonText = "覆盖当前方案",
                SecondaryButtonText = "新建导入方案",
                CloseButtonText = "取消",
                XamlRoot = _rootGrid.XamlRoot,
                RequestedTheme = _rootGrid.ActualTheme
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            return result switch
            {
                ContentDialogResult.Primary => ImportTargetChoice.OverwriteCurrent,
                ContentDialogResult.Secondary => ImportTargetChoice.CreateNewScheme,
                _ => ImportTargetChoice.Cancel
            };
        }

        private static string GetNextImportSchemeName()
        {
            var (schemes, _) = SchemeHelper.LoadSchemes();
            int max = 0;
            foreach (var s in schemes)
            {
                if (s?.Name == null)
                    continue;
                var name = s.Name.Trim();
                if (name == "导入方案")
                {
                    max = Math.Max(max, 1);
                    continue;
                }

                if (name.StartsWith("导入方案", StringComparison.Ordinal))
                {
                    var numPart = name.Substring("导入方案".Length).Trim();
                    if (int.TryParse(numPart, out var n))
                        max = Math.Max(max, n);
                }
            }

            return $"导入方案{Math.Max(1, max + 1)}";
        }

        private async Task ShowInlineMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = _rootGrid.XamlRoot,
                RequestedTheme = _rootGrid.ActualTheme
            };
            await ContentDialogGuard.ShowAsync(dialog);
        }
    }
}
