# CourseList

基于 **WinUI 3** 的 Windows 桌面应用：管理课程、查看网格课表、首页展示今日课程，并支持多课表方案、按周调课、教务系统导入等。

## 功能概要

- **首页**：今日课程卡片、欢迎与星期信息；方案切换后自动刷新。
- **课程表**：周视图、连续节次合并、拖放调课（当周 / 全局）、周次导航、`week-overrides` 按周覆盖。
- **课程列表**：搜索与多条件筛选、卡片式列表与编辑入口。
- **待办**：待办事项页面（当前占位，后续实现）。
- **设置**：主题、关闭到托盘、多方案、学期与节次时间、5/7 天课表、教务 WebView 导入。
- **托盘菜单**：支持“显示大窗口 / 显示小窗口 / 退出”，并与窗口模式切换动画联动。
- **窗口控制**：标题栏支持“一键置顶/取消置顶”（PinTop）。

完整说明（含数据文件、冲突规则、未来规划）见 **[项目功能说明.md](项目功能说明.md)**。

## 技术栈

| 项目 | 说明 |
|------|------|
| 运行时 | .NET 8（`net8.0-windows10.0.19041.0`） |
| UI | WinUI 3、Windows App SDK |
| 其他 | WebView2（导入）、CommunityToolkit.WinUI.Notifications |

## 环境要求

- **操作系统**：Windows 10 版本 **17763** 或更高（与 `TargetPlatformMinVersion` 一致）。
- **开发**：Visual Studio 2022，安装 **.NET 桌面开发** 与 **WinUI / Windows App SDK** 相关工作负载。

## 构建与运行

1. 使用 Visual Studio 打开 **`CourseList.csproj`**（若你有包含本项目的 `.sln`，也可打开解决方案），将该项目设为启动项目。
2. 选择目标平台（如 **x64**），按 **F5** 调试运行。

也可在仓库根目录使用 .NET CLI（需已安装相应工作负载/SDK）：

```powershell
dotnet build CourseList.csproj -c Debug
```

## 仓库结构（简要）

| 路径 | 说明 |
|------|------|
| `Models/` | `Course`、`WeekScheduleOverride` 等数据模型 |
| `Views/` | 各页面与 `ImportWebViewWindow`；详见 [Views/README.md](Views/README.md) |
| `Helpers/` | 配置、方案、课表、导入、托盘等；详见 [Helpers/README.md](Helpers/README.md) |
| `项目功能说明.md` | 功能与数据持久化详细说明 |

## 最近重构（窗口与托盘）

- `MainWindow` 持续瘦身：窗口模式切换动画已抽离到 `Helpers/Ui/WindowModeController`。
- 托盘流程已统一到 `Helpers/Platform/TrayMenuController`（托盘图标、右键菜单、隐藏/恢复状态）。
- `ContentDialogGuard` 保留为全局串行守卫，避免异步场景下多页面并发弹窗导致冲突。

## 数据目录（开发者注意）

应用数据根路径由 `Helpers/Core/PathHelper.cs` 中的 **`PathHelper.BaseFolder`** 决定。当前仓库内可能为**开发用固定路径**；发布或换机前请改为适合当前用户/安装目录的路径（参见 `项目功能说明.md` 中「可靠性与质量」规划）。

## 许可证

若仓库根目录未包含 `LICENSE` 文件，使用前请与项目维护者确认授权范围。
