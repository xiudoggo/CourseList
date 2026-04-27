using CourseList.Models;
using CourseList.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Microsoft.UI.Xaml.Shapes;

namespace CourseList.Views
{
    public sealed partial class SchedulePage : Page
    {
        private const double PeriodHeaderColumnWidth = 96;
        private const double DayColumnMinWidth = 150;
        private const double CourseContentWidth = 135;

        private List<Course> _courses = new List<Course>();
        private List<ScheduleBlock> _blocks = new List<ScheduleBlock>();
        // 5=周一到周五；7=周一到周日
        private int _scheduleWeekRange = 7;
        private int _periodCount = 11;
        private List<PeriodTimeRange> _periodTimeRanges = new List<PeriodTimeRange>();
        private DateTime _semesterStartMonday = DateTime.Today;
        private int _semesterTotalWeeks = 20;
        private int _displayWeek = 1;

        private bool _isCompactMode;
        private bool _isPageLoaded;

        // Compact: per-day canvas containers
        private readonly Dictionary<int, TextBlock> _compactDayHeaderDateTextByCol = new();
        private readonly Dictionary<int, Canvas> _compactDayCanvasByCol = new();
        private const double CompactTimeColumnWidth = 46;
        private const double CompactPeriodRowHeight = 68;

        // Compact mode adaptive sizing (only affects the compact container).
        private double _compactTimeColumnWidth = CompactTimeColumnWidth;
        private double _compactDayColumnWidth = 78;
        private double _lastCompactSizingWidth = double.NaN;
        private double _lastDesktopRenderWidth = double.NaN;
        private double _lastDesktopRenderHeight = double.NaN;
        private bool _desktopRenderPending;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _desktopResizeRenderTimer;
        private bool _desktopRerenderAfterLayoutPending;

        private List<WeekScheduleOverride> _weekOverrides = new();
        private Dictionary<(int courseId, int weekIndex), WeekScheduleOverride> _weekOverrideIndex = new();
        private const string DragPayloadKey = "CourseListScheduleDrag";

        private static readonly SolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Colors.White);
        private readonly Dictionary<string, SolidColorBrush> _courseBrushCache = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        private Rectangle? _desktopDragPreviewRect;
        private Border? _dragSourceBorder;
        private double _dragSourceOpacity = 1;
        private double _dragSourceScaleX = 0.95;
        private double _dragSourceScaleY = 0.95;

        // 小窗（compact）模式：单独的虚线落点预览层（不能复用 desktop 的 ScheduleGrid 坐标系）
        private Rectangle? _compactDragPreviewRect;
        private Canvas? _compactDragPreviewCurrentDayCanvas;

        /// <summary>拖拽过程中对连续行坐标 fr 做低通平滑，减轻虚线框在节次缝隙处抖动。</summary>
        private double _dragRowSmoothFr = double.NaN;
        private const double DragRowSmoothBlend = 0.26;

        private readonly Dictionary<Border, Border> _selectionOverlayMap = new();
        private Border? _selectedCourseBorder;
        private const string CourseScaleStoryboardKey = "CourseScaleStoryboard";

        public SchedulePage()
        {
            this.InitializeComponent();
            Loaded += SchedulePage_Loaded;
            Unloaded += SchedulePage_Unloaded;

            // 初始化时隐藏操作面板
            ExpandedPanel.Visibility = Visibility.Collapsed;

            // 小窗模式：随着窗口宽度变化需要重新分配紧凑容器列宽，确保周六/周日仍可完整显示。
            if (CompactScheduleContainer != null)
                CompactScheduleContainer.SizeChanged += CompactScheduleContainer_SizeChanged;
            if (DesktopScheduleBorder != null)
                DesktopScheduleBorder.SizeChanged += DesktopScheduleBorder_SizeChanged;
            if (DesktopBlocksCanvas != null)
                DesktopBlocksCanvas.SizeChanged += DesktopBlocksCanvas_SizeChanged;

            _desktopResizeRenderTimer = DispatcherQueue?.CreateTimer();
            if (_desktopResizeRenderTimer != null)
            {
                _desktopResizeRenderTimer.Interval = TimeSpan.FromMilliseconds(24);
                _desktopResizeRenderTimer.IsRepeating = false;
                _desktopResizeRenderTimer.Tick += DesktopResizeRenderTimer_Tick;
            }
        }

        private void SchedulePage_Unloaded(object sender, RoutedEventArgs e)
        {
            SchemeHelper.SchemeChanged -= OnSchemeChanged;
            if (CompactScheduleContainer != null)
                CompactScheduleContainer.SizeChanged -= CompactScheduleContainer_SizeChanged;
            if (DesktopScheduleBorder != null)
                DesktopScheduleBorder.SizeChanged -= DesktopScheduleBorder_SizeChanged;
            if (DesktopBlocksCanvas != null)
                DesktopBlocksCanvas.SizeChanged -= DesktopBlocksCanvas_SizeChanged;
        }

        private void OnSchemeChanged(object? sender, EventArgs e)
        {
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                var config = ConfigHelper.LoadConfig();
                _scheduleWeekRange = config.ScheduleWeekRange == 5 ? 5 : 7;
                _periodCount = config.PeriodCount;
                _periodTimeRanges = config.PeriodTimeRanges ?? new List<PeriodTimeRange>();
                _semesterTotalWeeks = config.SemesterTotalWeeks <= 0 ? 20 : config.SemesterTotalWeeks;
                _semesterStartMonday = NormalizeToMonday(config.SemesterStartMonday == default ? DateTime.Today : config.SemesterStartMonday);
                _displayWeek = GetWeekIndexByDate(DateTime.Today);
                if (_displayWeek < 1) _displayWeek = 1;
                if (_displayWeek > _semesterTotalWeeks) _displayWeek = _semesterTotalWeeks;
                ApplyWeekRangeVisibility();
                await LoadCoursesAsync();
                RebuildScheduleLayout();
                BuildScheduleGrid();
                UpdateWeekNavigationUi();

            // 切换页面首次进入时，Compact 容器尺寸可能还没测量出来，
            // 导致自适应宽度不生效。这里做一次延迟重试，确保周六/周日列完整。
            if (_isCompactMode)
                TryApplyCompactAdaptiveSizingAndRebuild(3);
            });
        }

        private async void SchedulePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            if (ScheduleSelectionTeachingTip != null)
                ScheduleSelectionTeachingTip.XamlRoot = XamlRoot;
            var config = ConfigHelper.LoadConfig();
            _scheduleWeekRange = config.ScheduleWeekRange == 5 ? 5 : 7;
            _periodCount = config.PeriodCount;
            _periodTimeRanges = config.PeriodTimeRanges ?? new List<PeriodTimeRange>();
            _semesterTotalWeeks = config.SemesterTotalWeeks <= 0 ? 20 : config.SemesterTotalWeeks;
            _semesterStartMonday = NormalizeToMonday(config.SemesterStartMonday == default ? DateTime.Today : config.SemesterStartMonday);
            _displayWeek = GetWeekIndexByDate(DateTime.Today);
            if (_displayWeek < 1) _displayWeek = 1;
            if (_displayWeek > _semesterTotalWeeks) _displayWeek = _semesterTotalWeeks;

            // 先根据配置重建列（周六/周日列删除或保留），再生成课程单元格
            ApplyWeekRangeVisibility();

            SchemeHelper.SchemeChanged += OnSchemeChanged;
            await LoadCoursesAsync();
            RebuildScheduleLayout();
            BuildScheduleGrid();
            UpdateWeekNavigationUi();

            // 进入课程表页时（从其它页面切过来）Compact 容器的 ActualWidth 可能还没稳定，
            // 导致列宽计算偏差从而渲染错位。这里做一次延迟自适应重建兜底。
            if (_isCompactMode)
                TryApplyCompactAdaptiveSizingAndRebuild(5);

            WireScheduleGridDragSurface();

            // 桌面模式：从其它页面首次进入/切回时，列宽可能尚未稳定，补一次延迟重绘修正对齐。
            if (!_isCompactMode)
                TryRerenderDesktopAfterLayout(6);
        }

        public void ApplyCompactMode(bool isCompact)
        {
            if (_isCompactMode == isCompact)
                return;

            _isCompactMode = isCompact;

            // 无论页面是否 Loaded，都先切换可见性，避免出现 compact 容器渲染不出来/残留桌面布局的情况。
            if (DesktopScheduleBorder != null)
                DesktopScheduleBorder.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
            if (CompactScheduleContainer != null)
                CompactScheduleContainer.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;

            // 小窗：不需要周次切换按钮/显示，也不需要展开操作栏。
            if (BottomControlsGrid != null)
                BottomControlsGrid.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;

            // 顶层兜底：即便 BottomControlsGrid 在某些场景未生效，也隐藏与周次/展开相关的控件
            var weekNavVisibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
            if (PrevWeekBtn != null) PrevWeekBtn.Visibility = weekNavVisibility;
            if (NextWeekBtn != null) NextWeekBtn.Visibility = weekNavVisibility;
            if (GoToCurrentWeekBtn != null) GoToCurrentWeekBtn.Visibility = weekNavVisibility;
            if (CurrentWeekText != null) CurrentWeekText.Visibility = weekNavVisibility;
            if (NotCurrentWeekHint != null) NotCurrentWeekHint.Visibility = weekNavVisibility;
            if (ExpandedPanel != null) ExpandedPanel.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;

            // 大窗：保留最小宽度；小窗：放开最小宽度（compact 使用另一套容器，但恢复桌面时要正确）。
            if (ScheduleGrid?.ColumnDefinitions != null && ScheduleGrid.ColumnDefinitions.Count >= 2)
            {
                for (int i = 1; i < ScheduleGrid.ColumnDefinitions.Count; i++)
                {
                    ScheduleGrid.ColumnDefinitions[i].MinWidth = isCompact ? 0 : DayColumnMinWidth;
                }
            }

            if (!_isPageLoaded)
                return;

            if (isCompact)
            {
                // 切入 compact 时先做一次自适应 sizing，保证周六/周日列能完整创建/显示。
                bool ok = ApplyCompactAdaptiveSizing();
                if (!ok)
                {
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        if (_isCompactMode)
                        {
                            ApplyCompactAdaptiveSizing();
                            BuildScheduleGrid();
                        }
                    });
                    return;
                }
            }

            BuildScheduleGrid();
        }

        private void CompactScheduleContainer_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (!_isCompactMode || !_isPageLoaded)
                return;

            double w = e.NewSize.Width;
            if (!double.IsNaN(_lastCompactSizingWidth) && Math.Abs(w - _lastCompactSizingWidth) < 6)
                return;

            _lastCompactSizingWidth = w;

            // SizeChanged 频繁触发，做一次宽度变化后的延迟重建，避免抖动/卡顿。
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isCompactMode)
                {
                    if (ApplyCompactAdaptiveSizing())
                        BuildScheduleGrid();
                }
            });
        }

        private void DesktopScheduleBorder_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (_isCompactMode || !_isPageLoaded)
                return;

            double w = e.NewSize.Width;
            double h = e.NewSize.Height;
            if (!double.IsNaN(_lastDesktopRenderWidth) &&
                !double.IsNaN(_lastDesktopRenderHeight) &&
                Math.Abs(w - _lastDesktopRenderWidth) < 6 &&
                Math.Abs(h - _lastDesktopRenderHeight) < 6)
            {
                return;
            }

            _lastDesktopRenderWidth = w;
            _lastDesktopRenderHeight = h;
            _desktopRenderPending = true;
            if (_desktopResizeRenderTimer == null)
            {
                if (_isCompactMode || !_isPageLoaded)
                    return;
                RenderDesktopBlocks();
                _desktopRenderPending = false;
                return;
            }

            _desktopResizeRenderTimer.Stop();
            _desktopResizeRenderTimer.Start();
        }

        private void DesktopResizeRenderTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            if (!_desktopRenderPending || _isCompactMode || !_isPageLoaded)
                return;
            _desktopRenderPending = false;
            RenderDesktopBlocks();
        }

        private void DesktopBlocksCanvas_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (_isCompactMode || !_isPageLoaded)
                return;

            // Canvas 实际承载宽度变化时（例如导航回来/首次测量），也需要触发一次节流重绘。
            _desktopRenderPending = true;
            if (_desktopResizeRenderTimer == null)
            {
                RenderDesktopBlocks();
                _desktopRenderPending = false;
                return;
            }

            _desktopResizeRenderTimer.Stop();
            _desktopResizeRenderTimer.Start();
        }

        private void TryRerenderDesktopAfterLayout(int attemptsLeft)
        {
            if (_isCompactMode || !_isPageLoaded)
                return;

            if (_desktopRerenderAfterLayoutPending && attemptsLeft < 2)
                return;

            _desktopRerenderAfterLayoutPending = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isCompactMode || !_isPageLoaded)
                {
                    _desktopRerenderAfterLayoutPending = false;
                    return;
                }

                if (IsDesktopLayoutWidthStable())
                {
                    _desktopRerenderAfterLayoutPending = false;
                    RenderDesktopBlocks();
                    return;
                }

                if (attemptsLeft > 1)
                {
                    TryRerenderDesktopAfterLayout(attemptsLeft - 1);
                    return;
                }

                _desktopRerenderAfterLayoutPending = false;
                RenderDesktopBlocks();
            });
        }

        private bool IsDesktopLayoutWidthStable()
        {
            try
            {
                if (DesktopBlocksCanvas == null || ScheduleGrid?.ColumnDefinitions == null)
                    return false;

                double canvasW = DesktopBlocksCanvas.ActualWidth;
                if (double.IsNaN(canvasW) || canvasW <= 20)
                    return false;

                int dayColCount = _scheduleWeekRange == 5 ? 5 : 7;
                if (ScheduleGrid.ColumnDefinitions.Count < 1 + dayColCount)
                    return false;

                double sum = 0;
                for (int c = 1; c <= dayColCount; c++)
                {
                    double w = ScheduleGrid.ColumnDefinitions[c].ActualWidth;
                    if (double.IsNaN(w) || w < 20)
                        return false;
                    sum += w;
                }

                // 容忍少量 rounding/scrollbar 的误差，但差太多说明还没稳定。
                return Math.Abs(sum - canvasW) <= 12;
            }
            catch
            {
                return false;
            }
        }

        private bool ApplyCompactAdaptiveSizing()
        {
            // 尝试用实际可视宽度计算紧凑容器的时间列/日列宽度。
            double viewportW = 0;

            if (CompactScrollViewer != null && CompactScrollViewer.ActualWidth > 0)
                viewportW = CompactScrollViewer.ActualWidth;
            else if (CompactScheduleContainer != null && CompactScheduleContainer.ActualWidth > 0)
                viewportW = CompactScheduleContainer.ActualWidth;

            if (viewportW <= 0)
                return false;

            bool showWeekend = _scheduleWeekRange == 7;
            int dayColumnCount = showWeekend ? 7 : 5;

            // 给出可伸缩但不过度占用的时间列宽度：当宽度足够时让日列也能被拉伸。
            // 同时留出一段余量给左侧“月份+第X周”的显示，避免挤压触发错位。
            double timeW = Math.Max(40, viewportW * 0.14);
            timeW = Math.Min(timeW, viewportW * 0.22);

            double paddingL = CompactDaysPanel?.Padding.Left ?? 0;
            double paddingR = CompactDaysPanel?.Padding.Right ?? 0;
            // 紧凑模式下我们希望“只留边界缝隙，不要列间缝隙”，Spacing 在 XAML 已设为 0。
            double spacingW = 0;

            // CompactLayoutGrid 右侧列宽是 (viewportW - timeW)，在其中再扣掉左右 padding。
            // 给一个小余量避免四舍五入导致的“差几个像素”溢出触发横向滚动。
            viewportW = Math.Max(0, viewportW - 2);
            double availableForDays = viewportW - timeW - paddingL - paddingR - spacingW;
            if (availableForDays <= 0)
                return false;

            double dayW = availableForDays / dayColumnCount;
            // 去掉上限，确保宽度足够时可以拉伸；只保留下限避免极端缩小时文字不可读。
            dayW = Math.Max(42, dayW);

            // 像素取整，避免 UI 四舍五入造成的 1~2px 溢出触发横向滚动。
            timeW = Math.Floor(timeW);
            dayW = Math.Floor(dayW);

            _compactTimeColumnWidth = timeW;
            _compactDayColumnWidth = dayW;

            // 同步更新 compact 布局的第一列宽度（时间轴列）。
            if (CompactLayoutGrid?.ColumnDefinitions != null && CompactLayoutGrid.ColumnDefinitions.Count >= 2)
            {
                CompactLayoutGrid.ColumnDefinitions[0].Width = new GridLength(_compactTimeColumnWidth, GridUnitType.Pixel);
            }

            // Spacing 固定为 XAML 的 0：只保留边界缝隙（通过内部 Border Margin）。

            return true;
        }

        private void TryApplyCompactAdaptiveSizingAndRebuild(int attemptsLeft)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_isCompactMode || !_isPageLoaded)
                    return;

                bool ok = ApplyCompactAdaptiveSizing();
                if (ok)
                {
                    BuildScheduleGrid();
                    return;
                }

                if (attemptsLeft > 1)
                {
                    TryApplyCompactAdaptiveSizingAndRebuild(attemptsLeft - 1);
                }
            });
        }

        private void WireScheduleGridDragSurface()
        {
            // Drag/Drop surface moved to DesktopBlocksCanvas (desktop) and per-day Canvas (compact).
            if (ScheduleGrid != null)
            {
                ScheduleGrid.AllowDrop = false;
            }

            if (DesktopBlocksCanvas != null)
            {
                DesktopBlocksCanvas.AllowDrop = true;
                DesktopBlocksCanvas.DragOver -= DesktopBlocksCanvas_DragOver;
                DesktopBlocksCanvas.DragOver += DesktopBlocksCanvas_DragOver;
                DesktopBlocksCanvas.Drop -= DesktopBlocksCanvas_Drop;
                DesktopBlocksCanvas.Drop += DesktopBlocksCanvas_Drop;
                DesktopBlocksCanvas.DragLeave -= DesktopBlocksCanvas_DragLeave;
                DesktopBlocksCanvas.DragLeave += DesktopBlocksCanvas_DragLeave;
            }
        }

        private void DesktopBlocksCanvas_DragLeave(object sender, DragEventArgs e)
        {
            ResetDragRowSmoothFr();
            ClearDesktopDragPreview();
        }

        private void DesktopBlocksCanvas_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.DataView?.Properties == null || !e.DataView.Properties.ContainsKey(DragPayloadKey))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                ClearDesktopDragPreview();
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;

            if (!TryParseDragPayload(e, out _, out _, out _, out int span))
            {
                ClearDesktopDragPreview();
                return;
            }

            var pos = e.GetPosition(DesktopBlocksCanvas);
            if (!TryResolveDesktopDropPlacement(pos, span, out int dayCol, out int startRow, isDragOver: true))
            {
                ClearDesktopDragPreview();
                return;
            }

            UpdateDesktopDragPreview(dayCol, startRow, span);
        }

        private async void DesktopBlocksCanvas_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            ClearDesktopDragPreview();

            if (e.DataView?.Properties == null ||
                !TryParseDragPayload(e, out int courseId, out int segStart, out int segEnd, out int span, out _))
            {
                ResetDragRowSmoothFr();
                return;
            }

            var pos = e.GetPosition(DesktopBlocksCanvas);
            if (!TryResolveDesktopDropPlacement(pos, span, out int targetCol, out int targetRow, isDragOver: false))
            {
                ResetDragRowSmoothFr();
                return;
            }

            ResetDragRowSmoothFr();
            await ProcessScheduleDropAsync(courseId, segStart, segEnd, targetCol, targetRow);
        }

        private bool TryResolveDesktopDropPlacement(Point positionInCanvas, int span, out int dayCol, out int startRow, bool isDragOver)
        {
            dayCol = 1;
            startRow = 1;
            span = Math.Max(1, span);

            int dc = _scheduleWeekRange == 5 ? 5 : 7;
            double totalW = DesktopBlocksCanvas?.ActualWidth ?? 0;
            if (totalW < 8 || dc <= 0)
                return false;

            // dayCol by x proportion (Canvas spans only day columns)
            double x = positionInCanvas.X;
            if (x < 0 || x > totalW)
                return false;
            double cellW = totalW / dc;
            dayCol = Math.Clamp((int)(x / cellW) + 1, 1, dc);

            double rowH = GetDesktopRowHeight();
            double frRaw = 1.0 + positionInCanvas.Y / Math.Max(8, rowH);
            frRaw = Math.Clamp(frRaw, 1.0, _periodCount + 1.0 - 1e-6);

            double fr = GetFrForPlacement(frRaw, isDragOver);
            startRow = ResolveStartRowFromFr(fr, span);

            int maxStart = _periodCount - span + 1;
            if (maxStart < 1)
                return false;
            startRow = Math.Clamp(startRow, 1, maxStart);
            return true;
        }

        private void UpdateDesktopDragPreview(int dayCol, int startRow, int span)
        {
            if (DesktopBlocksCanvas == null || _desktopDragPreviewRect == null)
                return;

            int dc = _scheduleWeekRange == 5 ? 5 : 7;
            double w = DesktopBlocksCanvas.ActualWidth;
            double h = DesktopBlocksCanvas.ActualHeight;
            if (w < 8 || h < 8)
                return;

            double cellW = w / dc;
            double cellH = GetDesktopRowHeight();

            Canvas.SetLeft(_desktopDragPreviewRect, (dayCol - 1) * cellW + 2);
            Canvas.SetTop(_desktopDragPreviewRect, (startRow - 1) * cellH + 2);
            _desktopDragPreviewRect.Width = Math.Max(8, cellW - 4);
            _desktopDragPreviewRect.Height = Math.Max(8, cellH * span - 4);
            _desktopDragPreviewRect.Visibility = Visibility.Visible;
        }

        private void ClearDesktopDragPreview()
        {
            if (_desktopDragPreviewRect != null)
                _desktopDragPreviewRect.Visibility = Visibility.Collapsed;
        }


        private async Task LoadCoursesAsync()
        {
            _courses = await CourseDataHelper.LoadCoursesAsync();
            _weekOverrides = await WeekScheduleOverrideHelper.LoadAsync();
            _weekOverrideIndex = ScheduleEffectiveHelper.BuildOverrideIndex(_weekOverrides);
            RebuildBlocks();
        }

        private void RebuildBlocks()
        {
            _blocks = ScheduleBlockBuilder.BuildBlocks(
                _courses,
                _displayWeek,
                _periodCount,
                _scheduleWeekRange,
                _semesterTotalWeeks,
                _weekOverrideIndex,
                _weekOverrides);
        }

        private void BuildScheduleGrid()
        {
            if (_isCompactMode)
                BuildCompactScheduleGrid();
            else
                BuildDesktopScheduleGrid();
        }

        private void BuildDesktopScheduleGrid()
        {
            EnsureDesktopCanvasInitialized();
            UpdateHeaderDates();

            RebuildBlocks();
            RenderDesktopBlocks();
            UpdateWeekNavigationUi();

            // 切换周次/切换周范围/首次进入：列宽可能稍后才稳定，补一次延迟重绘避免错位。
            TryRerenderDesktopAfterLayout(4);
        }

        private void BuildCompactScheduleGrid()
        {
            EnsureCompactScheduleInitialized();
            UpdateCompactTimeAxis();
            UpdateCompactHeaderDates();

            RebuildBlocks();
            RenderCompactBlocks();

            UpdateWeekNavigationUi();
        }

        private void EnsureDesktopCanvasInitialized()
        {
            if (DesktopBlocksCanvas == null)
                return;

            int dayColCount = _scheduleWeekRange == 5 ? 5 : 7;
            Grid.SetRow(DesktopBlocksCanvas, 1);
            Grid.SetColumn(DesktopBlocksCanvas, 1);
            Grid.SetRowSpan(DesktopBlocksCanvas, _periodCount);
            Grid.SetColumnSpan(DesktopBlocksCanvas, dayColCount);

            if (_desktopDragPreviewRect == null)
            {
                _desktopDragPreviewRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(220, 0, 120, 215)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(35, 0, 120, 215)),
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed
                };
                DesktopBlocksCanvas.Children.Add(_desktopDragPreviewRect);
            }
        }

        private double GetDesktopRowHeight()
        {
            try
            {
                if (ScheduleGrid?.RowDefinitions != null && ScheduleGrid.RowDefinitions.Count > 1)
                {
                    double h = ScheduleGrid.RowDefinitions[1].ActualHeight;
                    if (!double.IsNaN(h) && h >= 8)
                        return h;
                }
            }
            catch { }
            return 90;
        }

        private double GetDesktopDayColumnWidth(int dayCol)
        {
            // dayCol: 1..N maps to ScheduleGrid ColumnDefinitions[dayCol]
            try
            {
                if (ScheduleGrid?.ColumnDefinitions != null && ScheduleGrid.ColumnDefinitions.Count > dayCol)
                {
                    double w = ScheduleGrid.ColumnDefinitions[dayCol].ActualWidth;
                    if (!double.IsNaN(w) && w >= 8)
                        return w;
                }
            }
            catch { }
            return DayColumnMinWidth;
        }

        private double GetDesktopDayColumnLeft(int dayCol)
        {
            // within DesktopBlocksCanvas coordinate system (origin at dayCol=1 start)
            double x = 0;
            try
            {
                if (ScheduleGrid?.ColumnDefinitions == null)
                    return x;

                for (int c = 1; c < dayCol; c++)
                {
                    double w = ScheduleGrid.ColumnDefinitions.Count > c ? ScheduleGrid.ColumnDefinitions[c].ActualWidth : DayColumnMinWidth;
                    if (w < 1) w = DayColumnMinWidth;
                    x += w;
                }
            }
            catch { }
            return x;
        }

        private void RenderDesktopBlocks()
        {
            if (DesktopBlocksCanvas == null)
                return;

            // preserve preview rect (always last in z-order)
            var preview = _desktopDragPreviewRect;
            DesktopBlocksCanvas.Children.Clear();
            if (preview != null)
            {
                preview.Visibility = Visibility.Collapsed;
                DesktopBlocksCanvas.Children.Add(preview);
            }

            _selectionOverlayMap.Clear();
            _selectedCourseBorder = null;
            SelectedCourse = null;

            int dayColCount = _scheduleWeekRange == 5 ? 5 : 7;
            double rowH = GetDesktopRowHeight();

            foreach (var block in _blocks)
            {
                if (block.DayCol < 1 || block.DayCol > dayColCount)
                    continue;

                double colW = GetDesktopDayColumnWidth(block.DayCol);
                double left = GetDesktopDayColumnLeft(block.DayCol);
                double top = (block.StartPeriod - 1) * rowH;
                double height = block.Span * rowH;

                const double inset = 6;
                double w = Math.Max(24, colW - inset * 2);
                double h = Math.Max(18, height - inset * 2);

                var border = CreateCourseBlockElement(block, w, h, fontSizeTitle: 16, fontSizeSub: 14);
                Canvas.SetLeft(border, left + inset);
                Canvas.SetTop(border, top + inset);
                DesktopBlocksCanvas.Children.Add(border);
            }
        }

        private Border CreateCourseBlockElement(ScheduleBlock block, double width, double height, double fontSizeTitle, double fontSizeSub)
        {
            var course = block.CourseRef;
            var courseBrush = GetCourseBrush(course.Color);
            var textBrush = GetReadableTextBrush(courseBrush.Color);
            double opacity = block.IsActiveInWeek ? 1.0 : 0.35;

            var rootBorder = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(6),
                Background = courseBrush,
                Opacity = opacity,
                Tag = block,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 },
                IsHitTestVisible = true,
                CanDrag = block.IsActiveInWeek,
                AllowDrop = false
            };

            rootBorder.PointerPressed += CourseBlock_PointerPressed;
            rootBorder.DoubleTapped += CourseBlock_DoubleTapped;
            rootBorder.DragStarting += CourseBlock_DragStarting;
            rootBorder.DropCompleted += ScheduleCell_DropCompleted;

            var grid = new Grid();
            var sp = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 2,
                Margin = new Thickness(6, 4, 6, 4)
            };

            sp.Children.Add(new TextBlock
            {
                Text = course.Name,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                FontSize = fontSizeTitle,
                FontWeight = FontWeights.SemiBold
            });

            sp.Children.Add(new TextBlock
            {
                Text = course.Teacher,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                FontSize = fontSizeSub
            });

            sp.Children.Add(new TextBlock
            {
                Text = course.Classroom,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                FontSize = fontSizeSub
            });

            if (!block.IsActiveInWeek)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "（非本周）",
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = textBrush,
                    FontSize = Math.Max(10, fontSizeSub - 2),
                    FontWeight = FontWeights.SemiBold
                });
            }

            grid.Children.Add(sp);

            var overlay = new Border
            {
                IsHitTestVisible = false,
                CornerRadius = rootBorder.CornerRadius,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Opacity = 0,
                Margin = new Thickness(-2)
            };
            grid.Children.Add(overlay);
            _selectionOverlayMap[rootBorder] = overlay;

            rootBorder.Child = grid;

            if (block.IsActiveInWeek)
                _courseCellMap[rootBorder] = course;

            return rootBorder;
        }

        private static SolidColorBrush GetReadableTextBrush(Color background)
        {
            // 按相对亮度选择黑/白字：允许更浅、更丰富的课程色，同时保持文字可读。
            double r = background.R / 255.0;
            double g = background.G / 255.0;
            double b = background.B / 255.0;
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            return luminance >= 0.62
                ? new SolidColorBrush(Color.FromArgb(235, 18, 18, 18))
                : WhiteBrush;
        }

        private void DesktopBlocksCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var border = FindParentCourseBorder(source);
                if (border != null && _selectionOverlayMap.ContainsKey(border))
                    return;
            }

            ClearCourseSelection();
        }

        private void CourseBlock_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not ScheduleBlock block)
                return;

            // 已选中的卡片再次单击：不触发任何动画（避免“先缩小再放大”的闪动）。
            if (ReferenceEquals(border, _selectedCourseBorder))
                return;

            SelectCourse(block.CourseRef, border);
        }

        private async void CourseBlock_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not ScheduleBlock block)
                return;

            SelectedCourse = block.CourseRef;
            await EditCourseByCellAsync(block.CourseRef);
        }

        private void CourseBlock_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            if (sender is not Border border || border.Tag is not ScheduleBlock block)
            {
                e.Cancel = true;
                return;
            }

            int start = block.StartPeriod;
            int span = Math.Max(1, block.Span);
            int end = start + span - 1;

            e.Data.Properties[DragPayloadKey] = $"{block.CourseId}|{start}|{end}|{span}|{block.DayCol}";
            e.Data.RequestedOperation = DataPackageOperation.Move;

            _dragSourceBorder = border;
            _dragSourceOpacity = border.Opacity;
            if (border.RenderTransform is ScaleTransform st)
            {
                _dragSourceScaleX = st.ScaleX;
                _dragSourceScaleY = st.ScaleY;
            }

            _dragRowSmoothFr = double.NaN;
            AnimateDragSourceLift(border, lifting: true);
        }

        private static bool TryParseDragPayload(DragEventArgs e, out int courseId, out int segStart, out int segEnd, out int span, out int dayCol)
        {
            courseId = segStart = segEnd = span = dayCol = 0;
            if (e.DataView?.Properties == null ||
                !e.DataView.Properties.TryGetValue(DragPayloadKey, out var raw) ||
                raw is not string payload)
                return false;

            var parts = payload.Split('|');
            if (parts.Length >= 5 &&
                int.TryParse(parts[0], out courseId) &&
                int.TryParse(parts[1], out segStart) &&
                int.TryParse(parts[2], out segEnd) &&
                int.TryParse(parts[3], out span) &&
                int.TryParse(parts[4], out dayCol))
                return true;

            // fallback to legacy formats
            if (parts.Length >= 4 &&
                int.TryParse(parts[0], out courseId) &&
                int.TryParse(parts[1], out segStart) &&
                int.TryParse(parts[2], out segEnd) &&
                int.TryParse(parts[3], out span))
            {
                dayCol = 0;
                return true;
            }

            return false;
        }

        private void RenderCompactBlocks()
        {
            _courseCellMap.Clear();

            // clear canvases, keep preview rect only when needed
            foreach (var kv in _compactDayCanvasByCol)
            {
                var canvas = kv.Value;
                canvas.Children.Clear();
            }

            _selectionOverlayMap.Clear();
            _selectedCourseBorder = null;
            SelectedCourse = null;

            int dayColCount = _scheduleWeekRange == 5 ? 5 : 7;
            double rowH = CompactPeriodRowHeight;

            foreach (var block in _blocks)
            {
                if (block.DayCol < 1 || block.DayCol > dayColCount)
                    continue;
                if (!_compactDayCanvasByCol.TryGetValue(block.DayCol, out var canvas))
                    continue;

                double top = (block.StartPeriod - 1) * rowH;
                double height = block.Span * rowH;
                const double inset = 4;
                double w = Math.Max(30, _compactDayColumnWidth - inset * 2);
                double h = Math.Max(18, height - inset * 2);

                var border = CreateCourseBlockElement(block, w, h, fontSizeTitle: 13, fontSizeSub: 11);
                Canvas.SetLeft(border, inset);
                Canvas.SetTop(border, top + inset);
                canvas.Children.Add(border);
            }
        }

        private void CompactDayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var border = FindParentCourseBorder(source);
                if (border != null && _selectionOverlayMap.ContainsKey(border))
                    return;
            }
            ClearCourseSelection();
        }

        private void CompactDayCanvas_DragLeave(object sender, DragEventArgs e)
        {
            ResetDragRowSmoothFr();
            ClearCompactDragPreview();
        }

        private void CompactDayCanvas_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (e.DataView?.Properties == null || !e.DataView.Properties.ContainsKey(DragPayloadKey))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                ClearCompactDragPreview();
                return;
            }

            if (sender is not Canvas dayCanvas || dayCanvas.Tag is not int dayCol || dayCol < 1)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                ClearCompactDragPreview();
                return;
            }

            if (!TryParseDragPayload(e, out _, out _, out _, out int span))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                ClearCompactDragPreview();
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;

            var localPos = e.GetPosition(dayCanvas);
            double frRaw = 1.0 + localPos.Y / Math.Max(8, CompactPeriodRowHeight);
            frRaw = Math.Clamp(frRaw, 1.0, _periodCount + 1.0 - 1e-6);

            double fr = GetFrForPlacement(frRaw, isDragOver: true);
            int startRow = ResolveStartRowFromFr(fr, span);
            int maxStart = _periodCount - span + 1;
            if (maxStart < 1)
            {
                ClearCompactDragPreview();
                return;
            }
            startRow = Math.Clamp(startRow, 1, maxStart);
            UpdateCompactDragPreview(dayCanvas, dayCol, startRow, span);
        }

        private async void CompactDayCanvas_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (sender is not Canvas dayCanvas || dayCanvas.Tag is not int dayCol || dayCol < 1)
            {
                ResetDragRowSmoothFr();
                ClearCompactDragPreview();
                return;
            }

            if (e.DataView?.Properties == null ||
                !TryParseDragPayload(e, out int courseId, out int segStart, out int segEnd, out int span, out _))
            {
                ResetDragRowSmoothFr();
                ClearCompactDragPreview();
                return;
            }

            ClearCompactDragPreview();

            var localPos = e.GetPosition(dayCanvas);
            double frRaw = 1.0 + localPos.Y / Math.Max(8, CompactPeriodRowHeight);
            frRaw = Math.Clamp(frRaw, 1.0, _periodCount + 1.0 - 1e-6);

            double fr = GetFrForPlacement(frRaw, isDragOver: false);
            int targetRow = ResolveStartRowFromFr(fr, span);
            int maxStart = _periodCount - span + 1;
            if (maxStart < 1)
            {
                ResetDragRowSmoothFr();
                return;
            }
            targetRow = Math.Clamp(targetRow, 1, maxStart);

            ResetDragRowSmoothFr();
            await ProcessScheduleDropAsync(courseId, segStart, segEnd, dayCol, targetRow);
        }

        private void UpdateCompactDragPreview(Canvas dayCanvas, int dayCol, int startRow, int span)
        {
            if (_compactDragPreviewRect == null)
            {
                _compactDragPreviewRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(220, 0, 120, 215)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(35, 0, 120, 215)),
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed
                };
            }

            if (!ReferenceEquals(_compactDragPreviewCurrentDayCanvas, dayCanvas))
            {
                if (_compactDragPreviewRect.Parent is Panel p)
                    p.Children.Remove(_compactDragPreviewRect);
                dayCanvas.Children.Add(_compactDragPreviewRect);
                _compactDragPreviewCurrentDayCanvas = dayCanvas;
            }

            double rowH = CompactPeriodRowHeight;
            const double inset = 4;
            Canvas.SetLeft(_compactDragPreviewRect, inset);
            Canvas.SetTop(_compactDragPreviewRect, (startRow - 1) * rowH + inset);
            _compactDragPreviewRect.Width = Math.Max(8, _compactDayColumnWidth - inset * 2);
            _compactDragPreviewRect.Height = Math.Max(8, span * rowH - inset * 2);
            _compactDragPreviewRect.Visibility = Visibility.Visible;
        }

        private void ClearCompactDragPreview()
        {
            if (_compactDragPreviewRect != null)
                _compactDragPreviewRect.Visibility = Visibility.Collapsed;
            _compactDragPreviewCurrentDayCanvas = null;
        }

        private void EnsureCompactScheduleInitialized()
        {
            // 每次切换周次/模式时重建紧凑布局，避免旧尺寸/映射残留。
            CompactTimePanel.Children.Clear();
            CompactDaysPanel.Children.Clear();
            _compactDayHeaderDateTextByCol.Clear();
            _compactDayCanvasByCol.Clear();
            ClearCompactDragPreview();

            bool showWeekend = _scheduleWeekRange == 7;
            int dayColumnCount = showWeekend ? 7 : 5; // col=1..N

            double dayCanvasHeight = _periodCount * CompactPeriodRowHeight;

            for (int dayCol = 1; dayCol <= dayColumnCount; dayCol++)
            {
                var columnRoot = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = _compactDayColumnWidth
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2
                };

                header.Children.Add(new TextBlock
                {
                    Text = dayCol switch
                    {
                        1 => "周一",
                        2 => "周二",
                        3 => "周三",
                        4 => "周四",
                        5 => "周五",
                        6 => "周六",
                        7 => "周日",
                        _ => ""
                    },
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var dateText = new TextBlock
                {
                    Text = "",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                header.Children.Add(dateText);
                _compactDayHeaderDateTextByCol[dayCol] = dateText;
                columnRoot.Children.Add(header);

                var dayCanvas = new Canvas
                {
                    Width = _compactDayColumnWidth,
                    Height = dayCanvasHeight,
                    Background = TransparentBrush,
                    Margin = new Thickness(0, 6, 0, 0),
                    AllowDrop = true,
                    Tag = dayCol
                };
                dayCanvas.DragOver += CompactDayCanvas_DragOver;
                dayCanvas.Drop += CompactDayCanvas_Drop;
                dayCanvas.DragLeave += CompactDayCanvas_DragLeave;
                dayCanvas.PointerPressed += CompactDayCanvas_PointerPressed;

                _compactDayCanvasByCol[dayCol] = dayCanvas;
                columnRoot.Children.Add(dayCanvas);
                CompactDaysPanel.Children.Add(columnRoot);
            }
        }

        private void UpdateCompactTimeAxis()
        {
            CompactTimePanel.Children.Clear();

            // 顶部标题占位：与右侧每一天表头两行（周X + 日期）高度对齐，
            // 这样第一节“节次”就不会和课程格错位。
            var header = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };

            header.Children.Add(new TextBlock
            {
                Text = $"第{_displayWeek}周",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            });

            header.Children.Add(new TextBlock
            {
                Text = $"{DateTime.Now.Month}月",
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            });

            CompactTimePanel.Children.Add(header);

            // 每个 period 对应右侧 dayGrid 的一行：必须严格 1:1 对齐（不额外加 Margin）。
            const double periodFontSize = 12;
            double timeFontSize = Math.Max(9, periodFontSize - 1);

            for (int period = 1; period <= _periodCount; period++)
            {
                string startText = string.Empty;
                string endText = string.Empty;

                if (_periodTimeRanges.Count >= period)
                {
                    var ptr = _periodTimeRanges[period - 1];
                    if (ptr.StartTime.HasValue)
                        startText = ptr.StartTime.Value.ToString("HH\\:mm");
                    if (ptr.EndTime.HasValue)
                        endText = ptr.EndTime.Value.ToString("HH\\:mm");
                    else if (string.IsNullOrWhiteSpace(endText) && ptr.StartTime.HasValue)
                        endText = ptr.StartTime.Value.ToString("HH\\:mm");
                }

                var cell = new Grid
                {
                    Width = _compactTimeColumnWidth,
                    Height = CompactPeriodRowHeight,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                // 三行紧凑显示：用内层 StackPanel 来做“整体垂直居中”，Spacing=0 去掉行间空隙
                var inner = new StackPanel
                {
                    Margin = new Thickness(0, 6, 0, 0),//添加上边距
                    Padding = new Thickness(0, 6, 0, 0),//添加内边距
                    Orientation = Orientation.Vertical,
                    Spacing = 0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                inner.Children.Add(new TextBlock
                {
                    Text = $"{period}",
                    FontSize = periodFontSize,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                inner.Children.Add(new TextBlock
                {
                    Text = startText,
                    FontSize = timeFontSize,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                inner.Children.Add(new TextBlock
                {
                    Text = endText,
                    FontSize = timeFontSize,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                cell.Children.Add(inner);

                CompactTimePanel.Children.Add(cell);
            }
        }

        private void UpdateCompactHeaderDates()
        {
            var weekMonday = _semesterStartMonday.AddDays((_displayWeek - 1) * 7);

            foreach (var kv in _compactDayHeaderDateTextByCol)
            {
                int dayCol = kv.Key;
                int dayOffset = dayCol - 1; // 0=Mon
                var date = weekMonday.AddDays(dayOffset);
                kv.Value.Text = $"{date:M/d}";
            }
        }

        private void ApplyWeekRangeVisibility()
        {
            // 5天模式：真正删除 Grid 的周六/周日列，让周一到周五的 '*' 列自动铺满。
            // 同时隐藏 XAML 里的周六/周日表头（row=0, col>=6）。
            bool showWeekend = _scheduleWeekRange == 7;

            // 0: 节次/星期（固定宽度），1..5: 周一到周五，6..7: 周六到周日
            int dayColumnCount = showWeekend ? 7 : 5; // 仅指 1..N 这部分
            int totalColumnCount = 1 + dayColumnCount; // + col=0

            // 重建 ColumnDefinitions（避免残留空白列）
            ScheduleGrid.ColumnDefinitions.Clear();

            // col=0
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(PeriodHeaderColumnWidth),
                MinWidth = PeriodHeaderColumnWidth
            });

            // col=1..N
            for (int col = 1; col <= dayColumnCount; col++)
            {
                ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = DayColumnMinWidth
                });
            }

            // 隐藏 row=0 且落在被删除列上的表头元素
            if (!showWeekend)
            {
                foreach (var child in ScheduleGrid.Children)
                {
                    if (child is not FrameworkElement fe)
                        continue;

                    int row = Grid.GetRow(fe);
                    int col = Grid.GetColumn(fe);
                    if (row == 0 && col >= 6)
                        fe.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void RebuildScheduleLayout()
        {
            // 删除旧的动态内容：课程格 + 旧的“节次/时间”首列
            var toRemove = new List<UIElement>();
            foreach (var child in ScheduleGrid.Children)
            {
                if (child is not FrameworkElement fe)
                    continue;

                // row=0 是星期标题行，其它全部清掉再重建
                if (Grid.GetRow(fe) >= 1)
                    toRemove.Add(child);
            }

            foreach (var child in toRemove)
            {
                if (ReferenceEquals(child, DesktopBlocksCanvas))
                    continue;
                ScheduleGrid.Children.Remove(child);
            }

            ScheduleGrid.RowDefinitions.Clear();

            // header row
            ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            // period rows
            for (int row = 1; row <= _periodCount; row++)
                ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });

            _courseCellMap.Clear();

            BuildPeriodHeaderCells();
        }

        private void BuildPeriodHeaderCells()
        {
            var headerBg = this.Resources["ScheduleHeaderBrush"] as Brush;
            var headerTextBrush = this.Resources["ScheduleHeaderTextBrush"] as Brush;
            var cellStyle = this.Resources["CourseCellStyle"] as Style;

            for (int period = 1; period <= _periodCount; period++)
            {
                string timeText = _periodTimeRanges.Count >= period
                    ? _periodTimeRanges[period - 1].ToDisplayText()
                    : string.Empty;

                var border = new Border
                {
                    Style = cellStyle,
                    Background = headerBg,
                    BorderBrush = null,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0)
                };
                Grid.SetRow(border, period);
                Grid.SetColumn(border, 0);

                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var numText = new TextBlock
                {
                    Text = period.ToString(),
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var timeBlock = new TextBlock
                {
                    Text = timeText ?? string.Empty,
                    FontSize = 12,
                    Foreground = headerTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };

                if (string.IsNullOrWhiteSpace(timeText))
                    timeBlock.Visibility = Visibility.Collapsed;

                stack.Children.Add(numText);
                stack.Children.Add(timeBlock);

                border.Child = stack;
                ScheduleGrid.Children.Add(border);
            }
        }

        private static List<(int start, int end)> GetConsecutiveSegments(IReadOnlyList<int> sortedDistinctPeriods)
        {
            var segments = new List<(int start, int end)>();
            if (sortedDistinctPeriods == null || sortedDistinctPeriods.Count == 0)
                return segments;

            int segStart = sortedDistinctPeriods[0];
            int prev = sortedDistinctPeriods[0];

            for (int i = 1; i < sortedDistinctPeriods.Count; i++)
            {
                int p = sortedDistinctPeriods[i];
                if (p == prev + 1)
                {
                    prev = p;
                    continue;
                }

                segments.Add((segStart, prev));
                segStart = p;
                prev = p;
            }

            segments.Add((segStart, prev));
            return segments;
        }

        private SolidColorBrush GetCourseBrush(string? hex)
        {
            var key = string.IsNullOrWhiteSpace(hex) ? "#808080" : hex.Trim();
            if (_courseBrushCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var brush = new SolidColorBrush(ColorHelperFromHex(key));
            _courseBrushCache[key] = brush;
            return brush;
        }

        private void ScheduleCell_DropCompleted(UIElement sender, DropCompletedEventArgs e)
        {
            if (sender is Border b && ReferenceEquals(b, _dragSourceBorder))
            {
                AnimateDragSourceLift(b, lifting: false);
                _dragSourceBorder = null;
            }

            ResetDragRowSmoothFr();
            ClearDesktopDragPreview();
            ClearCompactDragPreview();
        }

        private void ResetDragRowSmoothFr() => _dragRowSmoothFr = double.NaN;

        private void BlendDragRowSmoothFr(double frRaw)
        {
            if (double.IsNaN(_dragRowSmoothFr))
                _dragRowSmoothFr = frRaw;
            else
                _dragRowSmoothFr = _dragRowSmoothFr * (1.0 - DragRowSmoothBlend) + frRaw * DragRowSmoothBlend;
        }

        private double GetFrForPlacement(double frRaw, bool isDragOver)
        {
            if (isDragOver)
            {
                BlendDragRowSmoothFr(frRaw);
                return _dragRowSmoothFr;
            }

            if (!double.IsNaN(_dragRowSmoothFr))
                return _dragRowSmoothFr * 0.55 + frRaw * 0.45;
            return frRaw;
        }

        private void AnimateDragSourceLift(Border border, bool lifting)
        {
            try
            {
                var storyboard = new Storyboard();
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(lifting ? 160 : 220);

                double toOp = lifting ? Math.Min(1, _dragSourceOpacity * 0.48) : _dragSourceOpacity;
                var opAnim = new DoubleAnimation
                {
                    From = border.Opacity,
                    To = toOp,
                    Duration = dur,
                    EasingFunction = ease
                };
                Storyboard.SetTarget(opAnim, border);
                Storyboard.SetTargetProperty(opAnim, "Opacity");
                storyboard.Children.Add(opAnim);

                if (border.RenderTransform is ScaleTransform st)
                {
                    double toSx = lifting ? 1.0 : _dragSourceScaleX;
                    double toSy = lifting ? 1.0 : _dragSourceScaleY;
                    var sxAnim = new DoubleAnimation
                    {
                        From = st.ScaleX,
                        To = toSx,
                        Duration = dur,
                        EasingFunction = ease
                    };
                    var syAnim = new DoubleAnimation
                    {
                        From = st.ScaleY,
                        To = toSy,
                        Duration = dur,
                        EasingFunction = ease
                    };
                    Storyboard.SetTarget(sxAnim, st);
                    Storyboard.SetTargetProperty(sxAnim, "ScaleX");
                    Storyboard.SetTarget(syAnim, st);
                    Storyboard.SetTargetProperty(syAnim, "ScaleY");
                    storyboard.Children.Add(sxAnim);
                    storyboard.Children.Add(syAnim);
                }

                storyboard.Begin();
            }
            catch
            {
                border.Opacity = lifting ? Math.Min(1, _dragSourceOpacity * 0.48) : _dragSourceOpacity;
                if (border.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = lifting ? 1.0 : _dragSourceScaleX;
                    st.ScaleY = lifting ? 1.0 : _dragSourceScaleY;
                }
            }
        }

        private static bool TryParseDragPayload(DragEventArgs e, out int courseId, out int segStart, out int segEnd, out int span)
        {
            return TryParseDragPayload(e, out courseId, out segStart, out segEnd, out span, out _);
        }


        private int ResolveStartRowFromFr(double fr, int span)
        {
            span = Math.Max(1, span);
            double startDouble = fr - (span - 1) / 2.0;
            return (int)Math.Round(startDouble);
        }


        private static DayOfWeek DayColumnToDayOfWeek(int col) => col switch
        {
            1 => DayOfWeek.Monday,
            2 => DayOfWeek.Tuesday,
            3 => DayOfWeek.Wednesday,
            4 => DayOfWeek.Thursday,
            5 => DayOfWeek.Friday,
            6 => DayOfWeek.Saturday,
            7 => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday
        };

        private static bool SegmentMatchesEffective(int segStart, int segEnd, List<int> effPeriods)
        {
            var sorted = effPeriods.Where(p => p >= 1).Distinct().OrderBy(p => p).ToList();
            var segments = GetConsecutiveSegments(sorted);
            return segments.Any(s => s.Item1 == segStart && s.Item2 == segEnd);
        }

        private static Course CloneCourseForConflict(Course c, DayOfWeek dow, List<int> periods) => new()
        {
            Id = c.Id,
            Name = c.Name,
            FromWeek = c.FromWeek,
            ToWeek = c.ToWeek,
            WeekType = c.WeekType,
            DayOfWeek = dow,
            ClassPeriods = periods.ToList()
        };

        private async Task ProcessScheduleDropAsync(int courseId, int segStart, int segEnd, int targetCol, int targetRow)
        {
            if (targetCol < 1 || targetRow < 1)
                return;

            var course = _courses.Find(c => c.Id == courseId);
            if (course == null)
                return;

            var (effDay, effPeriods) = ScheduleEffectiveHelper.GetEffectiveSlot(course, _displayWeek, _weekOverrideIndex, _weekOverrides);
            if (!SegmentMatchesEffective(segStart, segEnd, effPeriods))
                return;

            var targetDow = DayColumnToDayOfWeek(targetCol);
            bool sameWeekday = targetDow == effDay;
            var newPeriods = ScheduleEffectiveHelper.TryMoveSegment(
                effPeriods, segStart, segEnd, targetRow, _periodCount, sameWeekday);
            if (newPeriods == null)
            {
                ShowToast("无法移动到该位置（跨天多段课请使用编辑功能，或目标节次越界/重叠）");
                return;
            }

            var remaining = effPeriods.Where(p => p < segStart || p > segEnd).ToList();
            DayOfWeek newDow = remaining.Count > 0 ? effDay : targetDow;

            var effSorted = effPeriods.Distinct().OrderBy(p => p).ToList();
            if (newPeriods.SequenceEqual(effSorted) && newDow == effDay)
                return;

            var dialog = new ContentDialog
            {
                Title = "确认调课",
                Content = $"将「{course.Name}」调整为：{DayName(newDow)} 第 {string.Join("、", newPeriods)} 节。\n\n请选择生效范围：",
                PrimaryButtonText = $"仅第 {_displayWeek} 周",
                SecondaryButtonText = "全局",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };

            var result = await ContentDialogGuard.ShowAsync(dialog);
            if (result != ContentDialogResult.Primary && result != ContentDialogResult.Secondary)
                return;

            if (result == ContentDialogResult.Primary)
            {
                var temp = CloneCourseForConflict(course, newDow, newPeriods);
                var conflict = CourseConflictHelper.FindConflictCourseForWeek(
                    _courses, temp, _displayWeek, _semesterTotalWeeks, course.Id, _weekOverrides);
                if (conflict != null)
                {
                    await ShowConflictDialogAsync(temp, conflict);
                    return;
                }

                WeekScheduleOverrideHelper.Upsert(_weekOverrides, new WeekScheduleOverride
                {
                    CourseId = course.Id,
                    WeekIndex = _displayWeek,
                    DayOfWeek = newDow,
                    ClassPeriods = newPeriods
                });
                _weekOverrideIndex = ScheduleEffectiveHelper.BuildOverrideIndex(_weekOverrides);
                await WeekScheduleOverrideHelper.SaveAsync(_weekOverrides);
                BuildScheduleGrid();
                return;
            }

            if (result == ContentDialogResult.Secondary)
            {
                var temp = CloneCourseForConflict(course, newDow, newPeriods);
                var conflict = CourseConflictHelper.FindConflictCourse(_courses, temp, excludeCourseId: course.Id);
                if (conflict != null)
                {
                    await ShowConflictDialogAsync(temp, conflict);
                    return;
                }

                _weekOverrides.RemoveAll(o => o.CourseId == course.Id && o.WeekIndex == _displayWeek);
                _weekOverrideIndex = ScheduleEffectiveHelper.BuildOverrideIndex(_weekOverrides);
                await WeekScheduleOverrideHelper.SaveAsync(_weekOverrides);
                course.DayOfWeek = newDow;
                course.ClassPeriods = newPeriods;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                BuildScheduleGrid();
            }
        }

        private static string DayName(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => ""
        };

        private void CourseCell_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (_courseCellMap.TryGetValue(border, out var course))
            {
                SelectCourse(course, border);
            }
        }

        private async void CourseCell_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (_courseCellMap.TryGetValue(border, out var course))
            {
                await EditCourseByCellAsync(course);
            }
        }

        private Color ColorHelperFromHex(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex) || hex.Length < 7)
                    return Colors.Gray;
                return ColorHelper.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16)
                );
            }
            catch
            {
                return Colors.Gray;
            }
        }

        private static Color GetSelectionBorderColorFromCourse(Color courseColor)
        {
            // 让描边随课程色变化，同时保证在深/浅色上都足够可见：按亮度做轻微提亮/压暗。
            double r = courseColor.R / 255.0;
            double g = courseColor.G / 255.0;
            double b = courseColor.B / 255.0;
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            double t = luminance < 0.55 ? 0.35 : -0.35; // 暗色提亮，亮色压暗
            byte adj(byte c)
            {
                double v = c / 255.0;
                double outV = t >= 0 ? (v + (1 - v) * t) : (v * (1 + t));
                outV = Math.Clamp(outV, 0, 1);
                return (byte)Math.Round(outV * 255);
            }

            return Color.FromArgb(220, adj(courseColor.R), adj(courseColor.G), adj(courseColor.B));
        }

        #region 课程操作

        /// <summary>
        /// 获取当前选中的课程（用于编辑/删除）
        /// </summary>
        public Course? SelectedCourse { get; private set; }
        
        // 记录课程和单元格的映射
        private Dictionary<Border, Course> _courseCellMap = new();

        private async void AddCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowCourseFormAsync(null);
        }

        private async void EditCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCourse == null)
            {
                ShowScheduleSelectionTip(EditCourseBtn, "请先在课程表上点击选择要编辑的课程");
                return;
            }
            await ShowCourseFormAsync(SelectedCourse);
        }

        private async void DeleteCourseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCourse == null)
            {
                ShowScheduleSelectionTip(DeleteCourseBtn, "请先在课程表上点击选择要删除的课程");
                return;
            }

            // Flyout 形式确认删除（自动适配深/浅色主题）
            if (DeleteConfirmText != null)
                DeleteConfirmText.Text = $"确定要删除课程「{SelectedCourse.Name}」吗？";

            if (sender is FrameworkElement fe)
                FlyoutBase.ShowAttachedFlyout(fe);
        }

        private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmFlyout?.Hide();
        }

        private async void DeleteConfirmOk_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCourse == null)
            {
                DeleteConfirmFlyout?.Hide();
                ShowScheduleSelectionTip(DeleteCourseBtn, "请先在课程表上点击选择要删除的课程");
                return;
            }

            var id = SelectedCourse.Id;
            DeleteConfirmFlyout?.Hide();

            await DeleteCourseAsync(id);
            SelectedCourse = null;
            ShowToast("课程已删除");
        }

        private async Task ShowCourseFormAsync(Course? course)
        {
            var dialog = new CourseFormPage(course);
            dialog.XamlRoot = this.XamlRoot;
            
            var result = await ContentDialogGuard.ShowAsync(dialog);

            if (result == ContentDialogResult.Primary && dialog.NewCourse != null)
            {
                bool success = false;
                if (course == null)
                {
                    // 新增
                    success = await AddCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已添加");
                    }
                }
                else
                {
                    // 编辑
                    dialog.NewCourse.Id = course.Id; // 保持原有ID
                    success = await UpdateCourseAsync(dialog.NewCourse);
                    if (success)
                    {
                        ShowToast("课程已更新");
                    }
                }
            }
        }

        private async Task EditCourseByCellAsync(Course course)
        {
            SelectedCourse = course;
            await ShowCourseFormAsync(course);
        }

        public async Task<bool> AddCourseAsync(Course course)
        {
            // 冲突检测：同一天、节次重叠（且不是不同“周类型”的完全错开）
            var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: null);
            if (conflict != null)
            {
                await ShowConflictDialogAsync(course, conflict);
                return false;
            }

            _courses.Add(course);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            BuildScheduleGrid();
            return true;
        }

        public async Task DeleteCourseAsync(int courseId)
        {
            var target = _courses.Find(c => c.Id == courseId);
            if (target == null)
                return;

            WeekScheduleOverrideHelper.RemoveAllForCourse(_weekOverrides, courseId);
            _weekOverrideIndex = ScheduleEffectiveHelper.BuildOverrideIndex(_weekOverrides);
            await WeekScheduleOverrideHelper.SaveAsync(_weekOverrides);
            _courses.RemoveAll(c => c.Id == courseId);
            await CourseDataHelper.SaveCoursesAsync(_courses);
            BuildScheduleGrid();
        }

        public async Task<bool> UpdateCourseAsync(Course course)
        {
            var index = _courses.FindIndex(c => c.Id == course.Id);
            if (index >= 0)
            {
                // 冲突检测（排除自身）
                var conflict = CourseConflictHelper.FindConflictCourse(_courses, course, excludeCourseId: course.Id);
                if (conflict != null)
                {
                    await ShowConflictDialogAsync(course, conflict);
                    return false;
                }

                _courses[index] = course;
                await CourseDataHelper.SaveCoursesAsync(_courses);
                BuildScheduleGrid();
                return true;
            }

            return false;
        }

        private ContentDialog CreateSimpleDialog(string title, string content, string closeText = "确定")
        {
            return new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = closeText,
                XamlRoot = this.XamlRoot,
                RequestedTheme = this.ActualTheme
            };
        }

        private void ShowScheduleSelectionTip(FrameworkElement target, string subtitle)
        {
            ScheduleSelectionTeachingTip.Target = target;
            ScheduleSelectionTeachingTip.Subtitle = subtitle;
            ScheduleSelectionTeachingTip.XamlRoot = XamlRoot;
            ScheduleSelectionTeachingTip.IsOpen = true;
        }

        private void ShowToast(string message)
        {
            var toast = CreateSimpleDialog("提示", message);
            _ = ContentDialogGuard.ShowAsync(toast);
        }

        // FindConflictCourse 已抽取到 Helpers/CourseConflictHelper.cs

        /// <summary>
        /// 提示用户课程时间冲突
        /// </summary>
        private async Task ShowConflictDialogAsync(Course newCourse, Course conflictCourse)
        {
            var content =
                $"课程 \"{newCourse.Name}\" 的上课时间与已存在的课程 \"{conflictCourse.Name}\" 冲突。\n\n" +
                "请修改节次或周类型后再保存。";
            var dialog = CreateSimpleDialog("时间冲突", content);

            await ContentDialogGuard.ShowAsync(dialog);
        }

        /// <summary>
        /// 切换悬浮栏展开/收起状态
        /// </summary>
        private async void ToggleFloatBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ExpandedPanel.Visibility == Visibility.Collapsed)
            {
                await ExpandPanelAsync();
            }
            else
            {
                await CollapsePanelAsync();
            }
        }

        /// <summary>
        /// 展开操作面板（从右向左伸出）
        /// </summary>
        private async Task ExpandPanelAsync()
        {
            ExpandedPanel.Visibility = Visibility.Visible;
            
            // 获取 ScaleTransform
            var scaleTransform = (ScaleTransform)ExpandedPanel.RenderTransform;
            
            // 使用 XAML Storyboard 动画 ScaleTransform.ScaleX，时长适中（500ms），从 0 → 1
            scaleTransform.ScaleX = 0;
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, scaleTransform);
            Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);
            storyboard.Begin();
            
            // 展开后：让箭头指向右（表示“收起/返回”），且始终以中心旋转
            ToggleIconRotate.Angle = 180;
        }

        /// <summary>
        /// 收起操作面板（从左向右缩回）
        /// </summary>
        private async Task CollapsePanelAsync()
        {
            var scaleTransform = (ScaleTransform)ExpandedPanel.RenderTransform;
            
            // 使用 XAML Storyboard 动画 ScaleTransform.ScaleX，时长适中（500ms），从 1 → 0
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(350)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(animation, scaleTransform);
            Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);
            storyboard.Begin();

            // 等待动画结束后再隐藏（与动画时长保持一致）
            await Task.Delay(350);
            ExpandedPanel.Visibility = Visibility.Collapsed;
            scaleTransform.ScaleX = 0;
            
            // 收起后：恢复箭头指向左
            ToggleIconRotate.Angle = 0;
        }

        /// <summary>
        /// 选中课程（点击单元格时调用）
        /// </summary>
        private void SelectCourse(Course course, Border border)
        {
            // 已选中：不重复触发动画
            if (ReferenceEquals(border, _selectedCourseBorder) && ReferenceEquals(course, SelectedCourse))
                return;

            // 清除之前的选中状态（仅针对上一个选中项做回弹动画，避免全表遍历造成闪动/卡顿）
            ClearCourseSelection();

            // 设置新的选中状态：只做渲染层缩放与外描边，不改变布局测量
            SelectedCourse = course;
            _selectedCourseBorder = border;
            AnimateCourseScale(border, 1.05, 260);

            if (_selectionOverlayMap.TryGetValue(border, out var overlay))
            {
                var courseColor = ColorHelperFromHex(course.Color);
                overlay.BorderBrush = new SolidColorBrush(GetSelectionBorderColorFromCourse(courseColor));
                overlay.Opacity = 1.0;
            }
        }

        /// <summary>
        /// 清除当前课程选中状态（用于点击空白区域或切换选中）
        /// </summary>
        private void ClearCourseSelection()
        {
            var prevBorder = _selectedCourseBorder;
            SelectedCourse = null;
            _selectedCourseBorder = null;

            if (prevBorder != null)
            {
                AnimateCourseScale(prevBorder, 0.95, 200);
                if (_selectionOverlayMap.TryGetValue(prevBorder, out var overlay))
                    AnimateOverlayOpacity(overlay, to: 0.0, durationMs: 160);
            }
        }

        private static void AnimateOverlayOpacity(UIElement target, double to, int durationMs)
        {
            try
            {
                var sb = new Storyboard();
                var anim = new DoubleAnimation
                {
                    To = to,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, "Opacity");
                sb.Children.Add(anim);
                sb.Begin();
            }
            catch
            {
                target.Opacity = to;
            }
        }

        /// <summary>
        /// 为课程单元格执行缩放动画（用于选中/取消选中）
        /// </summary>
        private void AnimateCourseScale(Border border, double targetScale, int durationMs)
        {
            if (border == null)
                return;

            if (border.RenderTransform is not ScaleTransform scaleTransform)
            {
                scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                border.RenderTransform = scaleTransform;
                border.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }

            var storyboard = new Storyboard();

            // 关键：停止旧动画可能会把 ScaleTransform 属性立刻重置为基值（例如 0.95），
            // 如果从 Stop 之后才读取 fromX/fromY，会导致从点变成基值，从而“缩小动画消失/闪现”。
            // 因此先捕获当前值，再停止旧 storyboard，并在此之后把当前值写回，确保动画从正确的起点开始。
            double fromX = scaleTransform.ScaleX;
            double fromY = scaleTransform.ScaleY;

            if (border.Resources.TryGetValue(CourseScaleStoryboardKey, out var existing) &&
                existing is Storyboard existingStoryboard)
            {
                try { existingStoryboard.Stop(); } catch { }
            }

            // 避免 Stop 导致的瞬间回跳
            scaleTransform.ScaleX = fromX;
            scaleTransform.ScaleY = fromY;

            if (targetScale > Math.Max(fromX, fromY) + 0.0001)
            {
                double overshoot = Math.Max(targetScale, 1.05) + 0.03;
                var kx = new DoubleAnimationUsingKeyFrames();
                kx.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = fromX,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))
                });
                kx.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = overshoot,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(60, durationMs * 0.55))),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                kx.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = targetScale,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
                Storyboard.SetTarget(kx, scaleTransform);
                Storyboard.SetTargetProperty(kx, "ScaleX");

                var ky = new DoubleAnimationUsingKeyFrames();
                ky.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = fromY,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))
                });
                ky.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = overshoot,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(60, durationMs * 0.55))),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                ky.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = targetScale,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
                Storyboard.SetTarget(ky, scaleTransform);
                Storyboard.SetTargetProperty(ky, "ScaleY");

                storyboard.Children.Add(kx);
                storyboard.Children.Add(ky);
            }
            else
            {
                var animX = new DoubleAnimation
                {
                    From = fromX,
                    To = targetScale,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(animX, scaleTransform);
                Storyboard.SetTargetProperty(animX, "ScaleX");

                var animY = new DoubleAnimation
                {
                    From = fromY,
                    To = targetScale,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(animY, scaleTransform);
                Storyboard.SetTargetProperty(animY, "ScaleY");

                storyboard.Children.Add(animX);
                storyboard.Children.Add(animY);
            }

            border.Resources[CourseScaleStoryboardKey] = storyboard;
            storyboard.Begin();
        }

        /// <summary>
        /// 点击课程表空白区域时，取消选中
        /// </summary>
        private void ScheduleGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 判断点击是否落在某个课程卡片 Border 上，如果是则不清除选中
            if (e.OriginalSource is DependencyObject source)
            {
                var border = FindParentCourseBorder(source);
                if (border != null && _courseCellMap.ContainsKey(border))
                {
                    // 点击在课程组件内部，保持现有选中逻辑
                    return;
                }
            }

            // 点击在空白区域或非课程元素上，清除选中
            ClearCourseSelection();
        }

        /// <summary>
        /// 小窗竖版：点击空白区域取消选中（逻辑与桌面一致）。
        /// </summary>
        private void ScheduleCompact_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var border = FindParentCourseBorder(source);
                if (border != null && _courseCellMap.ContainsKey(border))
                    return;
            }

            ClearCourseSelection();
        }

        private Border? FindParentCourseBorder(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Border border && _courseCellMap.ContainsKey(border))
                {
                    return border;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private static DateTime NormalizeToMonday(DateTime date) =>
            SemesterWeekHelper.NormalizeToMonday(date);

        private int GetWeekIndexByDate(DateTime date) =>
            SemesterWeekHelper.GetWeekIndexByDate(date, _semesterStartMonday);

        private bool IsCourseActiveInWeek(Course course, int week) =>
            SemesterWeekHelper.IsCourseActiveInWeek(course, week, _semesterTotalWeeks);

        private void UpdateWeekNavigationUi()
        {
            if (CurrentWeekText == null || PrevWeekBtn == null || NextWeekBtn == null)
                return;

            var currentWeek = GetWeekIndexByDate(DateTime.Today);
            bool isCurrentWeek = _displayWeek == currentWeek;

            CurrentWeekText.Text = $"第 {_displayWeek} 周";
            PrevWeekBtn.IsEnabled = _displayWeek > 1;
            NextWeekBtn.IsEnabled = _displayWeek < _semesterTotalWeeks;
            if (GoToCurrentWeekBtn != null)
                GoToCurrentWeekBtn.IsEnabled = !isCurrentWeek;
            if (NotCurrentWeekHint != null)
                NotCurrentWeekHint.Visibility = isCurrentWeek ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateHeaderDates()
        {
            var weekMonday = _semesterStartMonday.AddDays((_displayWeek - 1) * 7);
            if (HeaderMonthText != null)
                HeaderMonthText.Text = $"{weekMonday.Month}月";

            if (HeaderDateMon != null) HeaderDateMon.Text = $"{weekMonday:M/d}";
            if (HeaderDateTue != null) HeaderDateTue.Text = $"{weekMonday.AddDays(1):M/d}";
            if (HeaderDateWed != null) HeaderDateWed.Text = $"{weekMonday.AddDays(2):M/d}";
            if (HeaderDateThu != null) HeaderDateThu.Text = $"{weekMonday.AddDays(3):M/d}";
            if (HeaderDateFri != null) HeaderDateFri.Text = $"{weekMonday.AddDays(4):M/d}";
            if (HeaderDateSat != null) HeaderDateSat.Text = $"{weekMonday.AddDays(5):M/d}";
            if (HeaderDateSun != null) HeaderDateSun.Text = $"{weekMonday.AddDays(6):M/d}";
        }

        private void PrevWeekBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_displayWeek <= 1)
                return;

            _displayWeek--;
            BuildScheduleGrid();
        }

        private void NextWeekBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_displayWeek >= _semesterTotalWeeks)
                return;

            _displayWeek++;
            BuildScheduleGrid();
        }

        private void GoToCurrentWeekBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!GoToCurrentWeekBtn.IsEnabled)
                return;
            var currentWeek = GetWeekIndexByDate(DateTime.Today);
            currentWeek = Math.Clamp(currentWeek, 1, _semesterTotalWeeks);
            _displayWeek = currentWeek;
            BuildScheduleGrid();
        }

        private void PrevWeekKeyboard_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (_displayWeek > 1)
            {
                _displayWeek--;
                BuildScheduleGrid();
                args.Handled = true;
            }
        }

        private void NextWeekKeyboard_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (_displayWeek < _semesterTotalWeeks)
            {
                _displayWeek++;
                BuildScheduleGrid();
                args.Handled = true;
            }
        }

        #endregion
    }
}
