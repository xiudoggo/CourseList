# Helpers 目录说明

所有类型仍在命名空间 **`CourseList.Helpers`** 下，仅按职责分子文件夹便于维护。

| 文件夹 | 内容 |
|--------|------|
| **Core/** | 应用数据根路径（`PathHelper`）、多方案与迁移（`SchemeHelper`）、方案内 `config.json`（`ConfigHelper`）、`courses.json` 加载与去抖保存（`CourseDataHelper`） |
| **Schedule/** | 按周有效节次与连续段（`ScheduleEffectiveHelper`）、学期周次与单双周（`SemesterWeekHelper`）、`week-overrides.json` 读写（`WeekScheduleOverrideHelper`）、课程时间冲突（`CourseConflictHelper`） |
| **Import/** | 教务 WebView 会话状态与页面脚本结果解析（`ImportSessionStore`、`CourseImportParser` 等，见 `CourseJwxtImport.cs`） |
| **Ui/** | 主题与标题栏按钮（`ThemeHelper`）、`ContentDialog` 串行弹出（`ContentDialogGuard`）、系统 Toast（`SystemNotificationHelper`） |
| **Platform/** | Win32 托盘图标（`Win32TrayIcon`） |

合并说明：

- **`ScheduleEffectiveHelper.cs`**：原「有效课表 + 连续段 + 拖放移动」与 **`SemesterWeekHelper`** 合并为同一文件，仍保留两个 `static class`，调用方无需改代码。
- **`Import/CourseJwxtImport.cs`**：原 **`ImportSessionStore`** 与 **`CourseImportParser`**（及解析用内部类型）合并为单文件，类型名未变。
