using System;
using System.Threading.Tasks;

namespace CourseList.Models;

/// <summary>
/// 置顶小窗内只读待办页的导航参数。
/// </summary>
public sealed class TodoPinSession
{
    public required int EditingId { get; init; }

    /// <summary>关闭当前标签。</summary>
    public required Action CloseTab { get; init; }

    /// <summary>完成状态变更或需要与主列表同步时调用（例如勾选完成）。</summary>
    public Func<Task>? AfterSaveAsync { get; init; }
}
