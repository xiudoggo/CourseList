using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CourseList.Views;
using Microsoft.UI.Xaml.Controls;

namespace CourseList.Helpers;

/// <summary>
/// 维护「待办 Id → 置顶窗标签」映射，并负责打开或聚焦。
/// </summary>
public static class TodoPinWindowManager
{
    public static event Action<int, bool>? TodoCompletedStateChanged;

    public static void NotifyTodoCompletedStateChanged(int todoId, bool completed)
    {
        try { TodoCompletedStateChanged?.Invoke(todoId, completed); } catch { }
    }

    /// <summary>最近一次激活的置顶窗（用于追加新标签）。</summary>
    private static TodoPinWindow? s_lastActiveWindow;
    private static readonly Dictionary<int, (TodoPinWindow Window, TabViewItem Tab)> s_openByTodoId = new();

    internal static void NotifyWindowActivated(TodoPinWindow window) => s_lastActiveWindow = window;

    internal static void NotifyWindowClosed(TodoPinWindow window)
    {
        if (ReferenceEquals(s_lastActiveWindow, window))
            s_lastActiveWindow = null;
        UnregisterWindowTabs(window);
    }

    internal static bool TryGetOpen(int todoId, out TodoPinWindow window, out TabViewItem tab)
    {
        if (s_openByTodoId.TryGetValue(todoId, out var v) && !v.Window.IsDisposed)
        {
            window = v.Window;
            tab = v.Tab;
            return true;
        }

        window = null!;
        tab = null!;
        return false;
    }

    internal static void RegisterTab(int todoId, TodoPinWindow window, TabViewItem tab) =>
        s_openByTodoId[todoId] = (window, tab);

    internal static void UnregisterTab(int todoId) => s_openByTodoId.Remove(todoId);

    internal static void UnregisterWindowTabs(TodoPinWindow window)
    {
        var ids = s_openByTodoId
            .Where(kv => ReferenceEquals(kv.Value.Window, window))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var id in ids)
            s_openByTodoId.Remove(id);
    }

    public static void CloseAllPinWindows()
    {
        var windows = s_openByTodoId.Values
            .Select(v => v.Window)
            .Distinct()
            .ToList();

        if (s_lastActiveWindow != null && !windows.Contains(s_lastActiveWindow))
            windows.Add(s_lastActiveWindow);

        foreach (var w in windows.Where(w => w is { IsDisposed: false }))
        {
            try { w.Close(); } catch { }
        }

        s_openByTodoId.Clear();
        s_lastActiveWindow = null;
    }

    /// <summary>
    /// 点击 pin：优先追加到最近激活的置顶窗；没有则新建。
    /// </summary>
    public static void OpenOrActivate(int todoId, string displayTitle, Func<Task> reloadMainListAsync)
    {
        if (TryGetOpen(todoId, out var existingWindow, out var existingTab))
        {
            existingWindow.EnsureActivated();
            existingWindow.ActivateAndSelectTab(existingTab);
            return;
        }

        TodoPinWindow win = s_lastActiveWindow is { IsDisposed: false } existing ? existing : new TodoPinWindow();
        win.EnsureActivated();
        win.AddTodoTab(todoId, string.IsNullOrEmpty(displayTitle) ? $"待办 #{todoId}" : displayTitle, reloadMainListAsync);
    }

}
