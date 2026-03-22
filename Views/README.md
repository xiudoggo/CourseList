# Views 目录说明

每个主要界面一个 **Page**（`.xaml` + `.xaml.cs`），由 `MainPage` 内 `NavigationView` 的 `Frame` 导航切换。

| 页面 | 职责 |
|------|------|
| **MainPage** | 主导航壳：侧栏菜单 + 内容 `Frame` |
| **HomePage** | 首页：今日课程概览 |
| **CourseListPage** | 课程列表：筛选、卡片、增删改入口 |
| **SchedulePage** | 周课表：网格渲染、选中、拖放调课、按周覆盖 |
| **CourseFormPage** | 课程新增/编辑表单 |
| **SettingsPage** | 主题、关闭行为、方案管理、学期与节次时间、教务导入入口 |
| **ImportWebViewWindow** | 独立窗口：内嵌 WebView2 登录教务并执行导入脚本 |

不设子文件夹：页面数量适中，按文件名即可区分。
