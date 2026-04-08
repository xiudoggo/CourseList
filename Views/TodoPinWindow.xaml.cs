using CourseList.Helpers;
using CourseList.Helpers.Platform;
using CourseList.Helpers.Ui;
using CourseList.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace CourseList.Views;

public sealed partial class TodoPinWindow : Window
{
    private static readonly HashSet<TodoPinWindow> s_openWindows = new();
    private static readonly object s_openWindowsLock = new();
    private static (TodoPinWindow Window, TabViewItem Tab, int TodoId)? s_dragContext;
    private static readonly object s_dragContextLock = new();

    private IntPtr _hwnd;
    private AppWindow? _appWindow;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private bool _sizeApplied;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>关闭或 Dispose 后为 true，避免向已关闭窗追加 Tab。</summary>
    public bool IsDisposed { get; private set; }

    public TodoPinWindow(bool autoActivate = true)
    {
        InitializeComponent();
        ThemeHelper.RegisterPinWindow(this);

        ExtendsContentIntoTitleBar = true;
        Title = "待办置顶";

        _hwnd = WindowNative.GetWindowHandle(this);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        try
        {
            if (_appWindow.Presenter is OverlappedPresenter op)
            {
                op.IsAlwaysOnTop = true;
                op.IsResizable = false;
                op.IsMaximizable = false;
                op.IsMinimizable = false;
                // Hide system title bar (keep border).
                op.SetBorderAndTitleBar(true, false);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        }
        catch
        {
            // ignore
        }

        ThemeHelper.SyncPinWindowChrome(this);

        // 注意：拖动区域位于 TabStripFooter 中，布局完成后再 SetTitleBar 更可靠。

        Activated += (_, args) =>
        {
            TodoPinWindowManager.NotifyWindowActivated(this);
            if (args.WindowActivationState != WindowActivationState.Deactivated)
                TryApplyClientSize500();
        };
        Closed += (_, _) =>
        {
            IsDisposed = true;
            lock (s_openWindowsLock)
                s_openWindows.Remove(this);
            ThemeHelper.UnregisterPinWindow(this);
            TodoPinWindowManager.NotifyWindowClosed(this);
            UnregisterAllTabs();
        };

        ApplyThemeFromMain();
        RootTabView.Loaded += RootTabView_Loaded;

        lock (s_openWindowsLock)
            s_openWindows.Add(this);

        if (autoActivate)
            Activate();
    }

    // NOTE: Dragging the window is handled by SetTitleBar(PinWindowDragRegion).
    // We intentionally don't manually capture PointerPressed here to avoid breaking TabView dragging.

    private void RootTabView_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeFromMain();
        TryApplyClientSize500();
        try
        {
            // 延后注册拖动矩形，避免首次布局时区域大小为 0。
            SetTitleBar(PinWindowDragRegion);
        }
        catch
        {
            // ignore
        }
    }

    // bottom close button removed; we rely on TabView item close.

    private void RootTabView_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            for (DependencyObject? o = e.OriginalSource as DependencyObject; o != null; o = VisualTreeHelper.GetParent(o))
            {
                if (o is TabViewItem)
                {
                    int delta = e.GetCurrentPoint(RootTabView).Properties.MouseWheelDelta;
                    if (delta == 0)
                        return;
                    int count = RootTabView.TabItems?.Count ?? 0;
                    if (count <= 1)
                        return;
                    int cur = RootTabView.SelectedIndex < 0 ? 0 : RootTabView.SelectedIndex;
                    int next = delta < 0 ? Math.Min(count - 1, cur + 1) : Math.Max(0, cur - 1);
                    if (next != cur)
                        RootTabView.SelectedIndex = next;
                    e.Handled = true;
                    return;
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void RootTabView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private const double InitialClientWidthDip = 500;
    private const double InitialClientHeightDip = 500;
    /// <summary>客户区初始约 500×500（以 DIP 为单位）。</summary>
    private void TryApplyClientSize500()
    {
        if (_sizeApplied || _appWindow == null)
            return;
        try
        {
            double scale = RootTabView.XamlRoot?.RasterizationScale ?? 1.0;
            int w = (int)Math.Round(InitialClientWidthDip * scale);
            int h = (int)Math.Round(InitialClientHeightDip * scale);

            // 与示例一致：调整客户区（物理像素）
            _appWindow.ResizeClient(new SizeInt32 { Width = w, Height = h });
            _sizeApplied = true;
        }
        catch
        {
            try
            {
                double scale = RootTabView.XamlRoot?.RasterizationScale ?? 1.0;
                int w = (int)Math.Round(InitialClientWidthDip * scale);
                int h = (int)Math.Round(InitialClientHeightDip * scale);
                _appWindow?.Resize(new SizeInt32 { Width = w, Height = h });
                _sizeApplied = true;
            }
            catch
            {
                // ignore
            }
        }
    }

    // Removed: window resizing clamps interfered with expected pointer behavior.
    private void AppWindow_Changed(object sender, AppWindowChangedEventArgs args) { }

    // Removed: WM hook not required; we rely on presenter/maximizable settings.
    private IntPtr PinWindowWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) => IntPtr.Zero;

    private void UnregisterAllTabs()
    {
        TodoPinWindowManager.UnregisterWindowTabs(this);
    }

    public void EnsureActivated() => Activate();

    public void ActivateAndSelectTab(TabViewItem tab)
    {
        Activate();
        RootTabView.SelectedItem = tab;
    }

    private void ApplyThemeFromMain()
    {
        if (Content is not FrameworkElement root)
            return;
        if (App.CurrentMainWindow?.Content is FrameworkElement main)
            root.RequestedTheme = main.RequestedTheme;
    }

    public void AddTodoTab(int todoId, string title, Func<Task> reloadMainListAsync)
    {
        var tab = new TabViewItem
        {
            Header = title,
            Tag = todoId,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            IsClosable = true
        };

        var tearFlyout = new MenuFlyout();
        var tearItem = new MenuFlyoutItem { Text = "在新窗口打开" };
        tearItem.Click += (_, _) => TearTabToNewWindow(tab);
        tearFlyout.Items.Add(tearItem);
        tab.ContextFlyout = tearFlyout;

        var session = new TodoPinSession
        {
            EditingId = todoId,
            // 当前置顶窗已提供窗口级关闭按钮；这里保留字段以满足会话类型。
            CloseTab = () => { },
            AfterSaveAsync = async () =>
            {
                await reloadMainListAsync();
                // 刷新“当前窗口/当前 tab”的标题（避免影响其它置顶窗口）。
                await RefreshTabHeaderAsync(tab, todoId);
            }
        };

        tab.Content = new TodoPinViewPage(session);

        RootTabView.TabItems.Add(tab);
        RootTabView.SelectedItem = tab;
        TodoPinWindowManager.RegisterTab(todoId, this, tab);
    }

    /// <summary>将已有 Tab（含 Frame 导航栈）迁入本窗口。</summary>
    public void AdoptTab(TabViewItem tab, int todoId)
    {
        if (RootTabView.TabItems.Contains(tab))
            return;
        tab.IsClosable = true;
        RootTabView.TabItems.Add(tab);
        RootTabView.SelectedItem = tab;
        if (tab.Tag is int id)
            TodoPinWindowManager.RegisterTab(id, this, tab);

        EnsureTabContextMenu(tab);
    }

    private void EnsureTabContextMenu(TabViewItem tab)
    {
        if (tab.ContextFlyout != null)
            return;
        var tearFlyout = new MenuFlyout();
        var tearItem = new MenuFlyoutItem { Text = "在新窗口打开" };
        tearItem.Click += (_, _) => TearTabToNewWindow(tab);
        tearFlyout.Items.Add(tearItem);
        tab.ContextFlyout = tearFlyout;
    }

    private void TearTabToNewWindow(TabViewItem tab)
    {
        if (tab.Tag is not int todoId)
            return;
        if (!RootTabView.TabItems.Contains(tab))
            return;

        POINT p;
        _ = GetCursorPos(out p);

        if (tab.Tag is int id)
            TodoPinWindowManager.UnregisterTab(id);
        RootTabView.TabItems.Remove(tab);

        var newWin = new TodoPinWindow();
        newWin.MoveToCursor();
        newWin.AdoptTab(tab, todoId);
        newWin.Activate();

        if (RootTabView.TabItems.Count == 0)
            Close();
    }

    /// <summary>将本窗口移到 anchor 附近（用于拖出拆分）。</summary>
    internal void MoveNear(AppWindow? anchor)
    {
        try
        {
            if (_appWindow == null || anchor == null)
                return;
            var r = anchor.Position;
            _appWindow.Move(new PointInt32(r.X + 48, r.Y + 48));
        }
        catch
        {
            // ignore
        }
    }

    internal void MoveToCursor()
    {
        try
        {
            if (_appWindow == null)
                return;
            _ = GetCursorPos(out POINT p);
            _appWindow.Move(new PointInt32(Math.Max(0, p.X - 60), Math.Max(0, p.Y - 24)));
        }
        catch
        {
            // ignore
        }
    }

    private static async Task RefreshTabHeaderAsync(TabViewItem tab, int todoId)
    {
        var todos = await ToDoDataHelper.LoadToDosAsync();
        var t = todos.FirstOrDefault(x => x.Id == todoId);
        tab.Header = t == null ? $"待办 #{todoId}" : (string.IsNullOrEmpty(t.Title) ? $"待办 #{todoId}" : t.Title);
    }

    internal void RemoveTab(TabViewItem tab)
    {
        if (tab.Tag is int id)
            TodoPinWindowManager.UnregisterTab(id);
        RootTabView.TabItems.Remove(tab);
        if (RootTabView.TabItems.Count == 0)
            Close();
    }

    private void RootTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is TabViewItem tab)
            RemoveTab(tab);
    }

    private void RootTabView_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        if (args.Tab is not TabViewItem tab || tab.Tag is not int todoId)
            return;

        lock (s_dragContextLock)
        {
            if (s_dragContext is { } ctx &&
                ReferenceEquals(ctx.Window, this) &&
                ReferenceEquals(ctx.Tab, tab))
            {
                // same gesture has already been handled by cross-window drop.
                if (!RootTabView.TabItems.Contains(tab))
                    return;
            }
        }

        // Don't split when there is only one tab in this window:
        // dropping to blank should return to origin instead of creating another window.
        if (sender.TabItems.Count <= 1)
        {
            return;
        }

        TearTabToNewWindow(tab);
    }

    private void RootTabView_TabStripDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void RootTabView_TabStripDrop(object sender, DragEventArgs e)
    {
        (TodoPinWindow Window, TabViewItem Tab, int TodoId)? ctx;
        lock (s_dragContextLock)
            ctx = s_dragContext;

        if (ctx is { } drag && !ReferenceEquals(drag.Window, this))
        {
            // Merge tab into this window.
            if (drag.Window.RootTabView.TabItems.Contains(drag.Tab))
                drag.Window.RootTabView.TabItems.Remove(drag.Tab);
            AdoptTab(drag.Tab, drag.TodoId);

            bool sourceEmptied = drag.Window.RootTabView.TabItems.Count == 0;
            if (sourceEmptied)
                drag.Window.Close();
        }
    }

    private void RootTabView_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (args.Tab is not TabViewItem tab || tab.Tag is not int todoId)
            return;

        lock (s_dragContextLock)
            s_dragContext = (this, tab, todoId);
    }

    private void RootTabView_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        lock (s_dragContextLock)
            s_dragContext = null;
    }
}

/// <summary>
/// 置顶 Tab 内展示的只读待办内容（与 <see cref="TodoPinWindow"/> 同文件，避免根目录与重复 XAML）。
/// </summary>
public sealed class TodoPinViewPage : Page
{
    private readonly TextBlock _titleText;
    private readonly CheckBox _completedCheckBox;
    private readonly SolidColorBrush _priorityBrush;
    private readonly TextBlock _priorityLabel;
    private readonly TextBlock _contentLabel;
    private readonly TextBlock _contentText;
    private readonly TextBlock _dueDateText;
    private readonly TextBlock _tagsHeader;
    private readonly StackPanel _tagsPanel;
    private readonly TagNameToBrushConverter _tagBrushConverter = new();

    private TodoPinSession? _session;
    private int _todoId;
    private bool _loading;
    private bool _syncSubscribed;

    public TodoPinViewPage(TodoPinSession? session)
    {
        _session = session;
        if (session != null)
            _todoId = session.EditingId;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled
        };
        var grid = new Grid
        {
            Padding = new Thickness(16),
            MaxWidth = 480,
            RowSpacing = 12
        };
        for (int i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "待办",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_titleText, 0);

        _completedCheckBox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = "标记为已完成"
        };
        _completedCheckBox.Checked += CompletedCheckBox_Changed;
        _completedCheckBox.Unchecked += CompletedCheckBox_Changed;

        _priorityBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        _priorityLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var priorityBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            MinWidth = 44,
            Background = _priorityBrush,
            Child = _priorityLabel
        };

        var row1 = new Grid { ColumnSpacing = 12 };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_completedCheckBox, 0);
        Grid.SetColumn(priorityBorder, 1);
        row1.Children.Add(_completedCheckBox);
        row1.Children.Add(priorityBorder);
        Grid.SetRow(row1, 1);

        _contentLabel = new TextBlock
        {
            Text = "内容",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12
        };
        Grid.SetRow(_contentLabel, 2);
        _contentLabel.Visibility = Visibility.Collapsed;

        _contentText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            IsTextSelectionEnabled = true
        };
        Grid.SetRow(_contentText, 3);

        _dueDateText = new TextBlock { FontSize = 13 };
        Grid.SetRow(_dueDateText, 4);

        _tagsHeader = new TextBlock
        {
            Text = "标签",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12
        };
        var tagsScroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _tagsPanel = new StackPanel { Spacing = 6, Orientation = Orientation.Horizontal };
        tagsScroll.Content = _tagsPanel;
        var tagsStack = new StackPanel { Spacing = 6 };
        tagsStack.Children.Add(_tagsHeader);
        tagsStack.Children.Add(tagsScroll);
        Grid.SetRow(tagsStack, 5);

        grid.Children.Add(_titleText);
        grid.Children.Add(row1);
        grid.Children.Add(_contentText);
        grid.Children.Add(_dueDateText);
        grid.Children.Add(tagsStack);

        scroll.Content = grid;
        Content = scroll;

        Loaded += async (_, _) =>
        {
            if (!_syncSubscribed)
            {
                TodoPinWindowManager.TodoCompletedStateChanged += OnTodoCompletedStateChanged;
                _syncSubscribed = true;
            }
            TodoPinViewPage_Loaded(this, new RoutedEventArgs());
            if (_session != null)
                await LoadTodoAsync();
        };
        Unloaded += (_, _) =>
        {
            if (_syncSubscribed)
            {
                TodoPinWindowManager.TodoCompletedStateChanged -= OnTodoCompletedStateChanged;
                _syncSubscribed = false;
            }
        };
    }

    private void OnTodoCompletedStateChanged(int changedTodoId, bool completed)
    {
        if (changedTodoId != _todoId || _loading)
            return;
        // Incremental sync only; avoid full reload.
        _loading = true;
        try
        {
            _completedCheckBox.IsChecked = completed;
        }
        finally
        {
            _loading = false;
        }
    }

    private void TodoPinViewPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ThemeResourceHelper.TryGetThemeBrush(this, "TextFillColorSecondaryBrush", out var sec) && sec != null)
        {
            _dueDateText.Foreground = sec;
            _tagsHeader.Foreground = sec;
        }

        if (ThemeResourceHelper.TryGetThemeBrush(this, "TextFillColorPrimaryBrush", out var pri) && pri != null)
            _contentText.Foreground = pri;
    }

    private async Task LoadTodoAsync()
    {
        _loading = true;
        try
        {
            var todos = await ToDoDataHelper.LoadToDosAsync();
            var todo = todos.FirstOrDefault(t => t.Id == _todoId);
            if (todo == null)
            {
                _titleText.Text = "待办已不存在";
                _contentText.Text = "该任务可能已在主窗口删除。";
                _completedCheckBox.IsEnabled = false;
                _dueDateText.Text = "";
                _priorityLabel.Text = "";
                RebuildTags(Array.Empty<string>());
                return;
            }

            _titleText.Text = string.IsNullOrEmpty(todo.Title) ? $"待办 #{todo.Id}" : todo.Title;
            _contentText.Text = string.IsNullOrEmpty(todo.Content) ? "（无内容）" : todo.Content;
            if (todo.DueDate.HasValue)
            {
                _dueDateText.Visibility = Visibility.Visible;
                _dueDateText.Text = todo.DueDateText;
            }
            else
            {
                _dueDateText.Visibility = Visibility.Collapsed;
                _dueDateText.Text = string.Empty;
            }
            _priorityLabel.Text = todo.PriorityDisplayName;
            _priorityBrush.Color = todo.PriorityEndColor;
            _completedCheckBox.IsChecked = todo.Completed;
            if (todo.Tags is { Count: > 0 })
            {
                _tagsHeader.Visibility = Visibility.Visible;
                _tagsPanel.Visibility = Visibility.Visible;
                RebuildTags(todo.Tags);
            }
            else
            {
                _tagsHeader.Visibility = Visibility.Collapsed;
                _tagsPanel.Visibility = Visibility.Collapsed;
                RebuildTags(Array.Empty<string>());
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildTags(IReadOnlyList<string> tags)
    {
        _tagsPanel.Children.Clear();
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;
            var brushObj = _tagBrushConverter.Convert(tag.Trim(), typeof(Brush), null!, string.Empty);
            var bg = brushObj as Brush ?? new SolidColorBrush(Color.FromArgb(255, 136, 136, 136));

            var border = new Border
            {
                MinHeight = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 400,
                Background = bg,
                BorderThickness = new Thickness(1)
            };
            if (ThemeResourceHelper.TryGetThemeBrush(this, "ControlStrokeColorDefaultBrush", out var stroke) && stroke != null)
                border.BorderBrush = stroke;

            border.Child = new TextBlock
            {
                Text = tag,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            _tagsPanel.Children.Add(border);
        }
    }

    private async void CompletedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _session == null)
            return;
        if (sender is not CheckBox cb)
            return;

        bool completed = cb.IsChecked == true;

        _loading = true;
        try
        {
            var todos = await ToDoDataHelper.LoadToDosAsync();

            // 只写回真实待办，避免拖拽占位（IsDragPlaceholder）污染 completed。
            var realTodos = todos.Where(t => !t.IsDragPlaceholder).ToList();

            // 如果当前 todoId 在真实数据中不存在，就不要写回。
            if (!realTodos.Any(t => t.Id == _todoId))
                return;

            var updated = new List<ToDoItem>(realTodos.Count);
            foreach (var t in realTodos)
            {
                bool isTarget = t.Id == _todoId;
                updated.Add(new ToDoItem
                {
                    Id = t.Id,
                    Title = t.Title,
                    Content = t.Content,
                    DueDate = t.DueDate,
                    Priority = t.Priority,
                    Tags = t.Tags?.ToList() ?? new List<string>(),
                    Completed = isTarget ? completed : t.Completed
                });
            }

            await ToDoDataHelper.SaveToDosAsync(updated);
            TodoPinWindowManager.NotifyTodoCompletedStateChanged(_todoId, completed);
            // Completion sync is handled via TodoCompletedStateChanged (incremental).
            // Avoid broad reload callback here; it causes all main cards to rebind/animate,
            // and can break after theme switching if the captured page instance is replaced.
        }
        finally
        {
            _loading = false;
        }
    }
}
