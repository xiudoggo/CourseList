using CourseList.Helpers;
using CourseList.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace CourseList.Views
{
    public sealed partial class ToDoPage : Page
    {
        private const double DragThresholdPx = 8;

        private readonly ObservableCollection<ToDoItem> _todos = new();
        private ToDoItem? _selectedToDo;
        private Border? _selectedCard;
        private readonly Thickness _desktopPadding = new Thickness(20);

        private Point _pressPointInRepeater;
        private bool _pressActive;
        private ToDoItem? _pressTodo;
        private uint _gesturePointerId;
        private bool _dragging;
        private ToDoItem? _dragItem;
        private ToDoItem? _dragPlaceholder;
        private Border? _dragGhost;
        private bool _gestureFinalized;
        private Size _dragGhostCellSize = new(248, 184);
        private int _slotMoveDeferCount;
        private const double HitInflateX = 6;
        private const double HitInflateYTop = 4;
        private const double HitInflateYBottom = 16;
        private Point _lastRepeaterPosForSlot;
        private bool _hasLastRepeaterPosForSlot;
        private int? _slotStabilityCandidate;
        private int _slotStabilityFrames;
        private const int SlotStabilityRequiredFrames = 3;
        private const double VerticalBiasDownPx = 22;
        private const double VerticalBiasUpPx = 12;
        private const double VerticalBiasMoveThreshold = 2;
        private readonly Dictionary<int, Storyboard> _flipStoryboards = new();
        private const int FlipDurationMs = 220;
        private Dictionary<int, Rect>? _pendingFlipBeforeRects;
        private Dictionary<int, int>? _pendingFlipOldIndexByKey;
        private EventHandler<object>? _flipLayoutWaiter;
        private EventHandler<object>? _mergeRevealLayoutWaiter;
        private ToDoItem? _mergeRevealItem;

        public ToDoPage()
        {
            this.InitializeComponent();
            ToDoRepeater.ItemsSource = _todos;
            Loaded += (_, _) =>
            {
                if (TodoSelectionTeachingTip != null)
                    TodoSelectionTeachingTip.XamlRoot = XamlRoot;
                if (DragOverlay != null)
                    DragOverlay.Clip = null;
                if (ToDoListHost != null)
                    ToDoListHost.Clip = null;
            };
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadToDosAsync();
        }

        public void ApplyCompactMode(bool isCompact)
        {
            if (ToDoMainGrid == null)
                return;

            ToDoMainGrid.Margin = isCompact ? new Thickness(12) : _desktopPadding;
        }

        private async Task LoadToDosAsync()
        {
            var items = await ToDoDataHelper.LoadToDosAsync();
            _todos.Clear();
            foreach (var o in items)
                _todos.Add(o);
            ClearSelectionVisual();
            _selectedToDo = null;
            UpdateEmptyVisibility();
        }

        private void UpdateEmptyVisibility()
        {
            ToDoEmptyText.Visibility = _todos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private System.Collections.Generic.List<ToDoItem> SnapshotRealTodos() =>
            _todos.Where(t => !t.IsDragPlaceholder).ToList();

        private async Task SaveRealTodosAsync()
        {
            await ToDoDataHelper.SaveToDosAsync(SnapshotRealTodos());
        }

        private void AddTodoBtn_Click(object sender, RoutedEventArgs e)
        {
            Frame?.Navigate(typeof(ToDoFormPage));
        }

        private void EditTodoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToDo == null || _selectedToDo.IsDragPlaceholder)
            {
                ShowTodoSelectionTip(EditTodoBtn, "请先在列表中点击选择一个待办项");
                return;
            }

            Frame?.Navigate(typeof(ToDoFormPage), _selectedToDo.Id);
        }

        private async void DeleteTodoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToDo == null || _selectedToDo.IsDragPlaceholder)
            {
                ShowTodoSelectionTip(DeleteTodoBtn, "请先在列表中点击选择一个待办项");
                return;
            }

            if (DeleteConfirmText != null)
                DeleteConfirmText.Text = $"确定要删除待办「{_selectedToDo.Title}」吗？";

            if (sender is FrameworkElement fe)
                FlyoutBase.ShowAttachedFlyout(fe);
        }

        private void DeleteConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmFlyout?.Hide();
        }

        private async void DeleteConfirmOk_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToDo == null || _selectedToDo.IsDragPlaceholder)
            {
                DeleteConfirmFlyout?.Hide();
                ShowTodoSelectionTip(DeleteTodoBtn, "请先在列表中点击选择一个待办项");
                return;
            }

            int id = _selectedToDo.Id;
            DeleteConfirmFlyout?.Hide();
            ClearSelectionVisual();
            _selectedToDo = null;
            var toRemove = _todos.FirstOrDefault(t => t.Id == id);
            if (toRemove != null)
                _todos.Remove(toRemove);
            await SaveRealTodosAsync();
            UpdateEmptyVisibility();
        }

        private async void ToDoCompleted_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb)
                return;
            if (TryResolveToDoItem(cb) is not { } bound || bound.IsDragPlaceholder)
                return;
            var item = _todos.FirstOrDefault(t => t.Id == bound.Id);
            if (item == null || item.IsDragPlaceholder)
                return;

            bool newValue = cb.IsChecked == true;
            item.Completed = newValue;
            item.UpdatedAt = DateTime.Now;
            await SaveRealTodosAsync();
        }

        private static ToDoItem? TryResolveToDoItem(CheckBox cb)
        {
            if (cb.DataContext is ToDoItem dc)
                return dc;
            for (var o = (DependencyObject?)cb; o != null; o = VisualTreeHelper.GetParent(o))
            {
                if (o is FrameworkElement fe && fe.Tag is ToDoItem todo)
                    return todo;
            }
            return null;
        }

        private void ToDoCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (PointerEventOriginatedInCheckBox(e))
                return;
            if (sender is not Border card || card.Tag is not ToDoItem todo || todo.IsDragPlaceholder)
                return;

            if (VisualTreeHelper.GetParent(card) is FrameworkElement itemRoot
                && itemRoot.ActualWidth > 1
                && itemRoot.ActualHeight > 1)
            {
                _dragGhostCellSize = new Size(itemRoot.ActualWidth, itemRoot.ActualHeight);
            }
            else if (card.ActualWidth > 1 && card.ActualHeight > 1)
            {
                _dragGhostCellSize = new Size(card.ActualWidth + 12, card.ActualHeight + 12);
            }

            _pressPointInRepeater = GetPointerPositionInRepeater(e);
            _pressActive = true;
            _pressTodo = todo;
            _gesturePointerId = e.Pointer.PointerId;
            _dragging = false;
            _dragItem = null;
            _dragPlaceholder = null;
            _gestureFinalized = false;
            _slotMoveDeferCount = 0;
            _hasLastRepeaterPosForSlot = false;
            _slotStabilityCandidate = null;
            _slotStabilityFrames = 0;
            ToDoListHost.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void ToDoListHost_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_pressActive || e.Pointer.PointerId != _gesturePointerId)
                return;

            var pos = GetPointerPositionInRepeater(e);
            double dx = pos.X - _pressPointInRepeater.X;
            double dy = pos.Y - _pressPointInRepeater.Y;

            if (!_dragging && dx * dx + dy * dy < DragThresholdPx * DragThresholdPx)
                return;

            if (!_dragging && _pressTodo != null)
            {
                _dragging = true;
                ClearSelectionVisual();
                _selectedToDo = null;
                BeginDragWithPlaceholder(_pressTodo);
                if (_dragPlaceholder == null)
                {
                    _dragging = false;
                    return;
                }

                EnsureDragGhost(_pressTodo);
            }

            if (_dragging)
            {
                UpdateDragGhostPosition(e);
                if (_slotMoveDeferCount > 0)
                    _slotMoveDeferCount--;
                else if (AreRepeaterCellsLayoutReady())
                {
                    var posForSlot = ApplyVerticalBiasForSlotDetection(pos);
                    int slot = ComputeDropSlotFromPoint(posForSlot);
                    MaybeCommitPlaceholderSlot(slot);
                }

                _lastRepeaterPosForSlot = pos;
                _hasLastRepeaterPosForSlot = true;
            }

            e.Handled = true;
        }

        private async void ToDoListHost_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_pressActive || e.Pointer.PointerId != _gesturePointerId)
                return;

            FinalizeGesture(e);
            await CompleteAsync();
        }

        private async void ToDoListHost_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_pressActive || e.Pointer.PointerId != _gesturePointerId)
                return;

            if (_gestureFinalized)
                return;

            FinalizeGesture(null);
            await CompleteAsync();
        }

        private void FinalizeGesture(PointerRoutedEventArgs? e)
        {
            if (_gestureFinalized)
                return;
            _gestureFinalized = true;

            try
            {
                if (e != null)
                    ToDoListHost.ReleasePointerCapture(e.Pointer);
            }
            catch { /* 已释放 */ }
        }

        private async Task CompleteAsync()
        {
            ToDoItem? merged = null;
            try
            {
                if (_dragging)
                {
                    merged = _dragItem;
                    MergePlaceholderAndItem();
                    RemoveDragGhost();
                    await SaveRealTodosAsync();
                }
                else if (_pressTodo != null && !_pressTodo.IsDragPlaceholder)
                {
                    var br = FindTodoCardBorder(_pressTodo);
                    if (br != null)
                    {
                        ClearSelectionVisual();
                        _selectedToDo = _pressTodo;
                        _selectedCard = br;
                        ApplyTodoCardSelectedVisual(br, selected: true);
                        AnimateCardHoverMetrics(br, 1.03, 0, 100);
                    }
                }
            }
            finally
            {
                ResetGestureState();
            }

            if (merged != null)
                ScheduleMergeReveal(merged);
            UpdateEmptyVisibility();
        }

        private void ResetGestureState()
        {
            CancelPendingFlipLayout();
            ClearRepeaterSlideState();
            _pressActive = false;
            _pressTodo = null;
            _dragging = false;
            _dragItem = null;
            _dragPlaceholder = null;
            RemoveDragGhost();
            _slotMoveDeferCount = 0;
            _hasLastRepeaterPosForSlot = false;
            _slotStabilityCandidate = null;
            _slotStabilityFrames = 0;
        }

        /// <summary>
        /// 下移时略增大 Y，减轻「正下方被判成上一格」；上移略减小 Y。
        /// </summary>
        private Point ApplyVerticalBiasForSlotDetection(Point pos)
        {
            if (!_hasLastRepeaterPosForSlot)
                return pos;
            double dy = pos.Y - _lastRepeaterPosForSlot.Y;
            if (dy > VerticalBiasMoveThreshold)
                return new Point(pos.X, pos.Y + VerticalBiasDownPx);
            if (dy < -VerticalBiasMoveThreshold)
                return new Point(pos.X, pos.Y - VerticalBiasUpPx);
            return pos;
        }

        /// <summary>
        /// 连续多帧得到同一目标槽位才提交，避免边界处两槽来回跳导致占位与 FLIP 屏闪。
        /// </summary>
        private void MaybeCommitPlaceholderSlot(int candidateSlot)
        {
            if (_dragPlaceholder == null)
                return;
            int phIdx = _todos.IndexOf(_dragPlaceholder);
            if (phIdx < 0)
                return;
            int n = _todos.Count;
            candidateSlot = Math.Clamp(candidateSlot, 0, n);
            if (candidateSlot == phIdx || candidateSlot == phIdx + 1)
            {
                _slotStabilityCandidate = null;
                _slotStabilityFrames = 0;
                return;
            }

            if (_slotStabilityCandidate == candidateSlot)
                _slotStabilityFrames++;
            else
            {
                _slotStabilityCandidate = candidateSlot;
                _slotStabilityFrames = 1;
            }

            if (_slotStabilityFrames < SlotStabilityRequiredFrames)
                return;

            MovePlaceholderToSlot(candidateSlot);
            _slotStabilityCandidate = null;
            _slotStabilityFrames = 0;
        }

        /// <summary>
        /// 经 ScrollViewer 视口映射到 Repeater，与格子 bounds（相对 Repeater）一致；
        /// 仅用 GetCurrentPoint(Repeater) 时竖直方向易与 bounds 差一行。
        /// </summary>
        private Point GetPointerPositionInRepeater(PointerRoutedEventArgs e)
        {
            var pViewport = e.GetCurrentPoint(ToDoScrollViewer).Position;
            var gt = ToDoScrollViewer.TransformToVisual(ToDoRepeater);
            return gt.TransformPoint(pViewport);
        }

        private void BeginDragWithPlaceholder(ToDoItem item)
        {
            int idx = _todos.IndexOf(item);
            if (idx < 0)
                return;

            _dragItem = item;
            _dragPlaceholder = ToDoItem.CreateDragPlaceholder();
            _todos.RemoveAt(idx);
            _todos.Insert(idx, _dragPlaceholder);
            _slotMoveDeferCount = 2;
            _hasLastRepeaterPosForSlot = false;
            _slotStabilityCandidate = null;
            _slotStabilityFrames = 0;
        }

        private bool AreRepeaterCellsLayoutReady()
        {
            if (_todos.Count == 0 || _dragPlaceholder == null)
                return true;
            int ph = _todos.IndexOf(_dragPlaceholder);
            if (ph < 0)
                return true;
            return ToDoRepeater.TryGetElement(ph) is FrameworkElement fe
                   && fe.ActualWidth >= 8
                   && fe.ActualHeight >= 8;
        }

        private void MovePlaceholderToSlot(int slot)
        {
            if (_dragPlaceholder == null)
                return;

            int phIdx = _todos.IndexOf(_dragPlaceholder);
            if (phIdx < 0)
                return;

            int n = _todos.Count;
            slot = Math.Clamp(slot, 0, n);
            if (slot == phIdx || slot == phIdx + 1)
                return;

            var beforeRects = CaptureItemBoundsByKey();
            var oldIndexByKey = CaptureItemIndexByKey();

            _todos.RemoveAt(phIdx);
            int insertAt = slot > phIdx ? slot - 1 : slot;
            insertAt = Math.Clamp(insertAt, 0, _todos.Count);
            _todos.Insert(insertAt, _dragPlaceholder);

            ScheduleSelectiveFlip(beforeRects, oldIndexByKey);
        }

        private static int ItemSlideKey(ToDoItem item) =>
            item.IsDragPlaceholder ? int.MinValue : item.Id;

        private Dictionary<int, Rect> CaptureItemBoundsByKey()
        {
            var d = new Dictionary<int, Rect>();
            for (int i = 0; i < _todos.Count; i++)
            {
                if (ToDoRepeater.TryGetElement(i) is not FrameworkElement fe)
                    continue;
                int key = ItemSlideKey(_todos[i]);
                d[key] = fe.TransformToVisual(ToDoRepeater)
                    .TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            }

            return d;
        }

        private Dictionary<int, int> CaptureItemIndexByKey()
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i < _todos.Count; i++)
                d[ItemSlideKey(_todos[i])] = i;
            return d;
        }

        private void ScheduleSelectiveFlip(Dictionary<int, Rect> beforeRects, Dictionary<int, int> oldIndexByKey)
        {
            _pendingFlipBeforeRects = beforeRects;
            _pendingFlipOldIndexByKey = oldIndexByKey;

            if (_flipLayoutWaiter != null)
                return;

            _flipLayoutWaiter = OnFlipLayoutWaiter;
            ToDoRepeater.LayoutUpdated += _flipLayoutWaiter;
        }

        private void OnFlipLayoutWaiter(object? sender, object e)
        {
            if (_flipLayoutWaiter != null)
                ToDoRepeater.LayoutUpdated -= _flipLayoutWaiter;
            _flipLayoutWaiter = null;

            var before = _pendingFlipBeforeRects;
            var oldIdx = _pendingFlipOldIndexByKey;
            _pendingFlipBeforeRects = null;
            _pendingFlipOldIndexByKey = null;

            if (before == null || oldIdx == null)
                return;

            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq == null)
            {
                RunSelectiveFlipAnimation(before, oldIdx);
                return;
            }

            dq.TryEnqueue(DispatcherQueuePriority.Low, () => RunSelectiveFlipAnimation(before, oldIdx));
        }

        private void CancelPendingFlipLayout()
        {
            if (_flipLayoutWaiter != null)
            {
                ToDoRepeater.LayoutUpdated -= _flipLayoutWaiter;
                _flipLayoutWaiter = null;
            }

            _pendingFlipBeforeRects = null;
            _pendingFlipOldIndexByKey = null;
        }

        /// <summary>
        /// 仅对「列表索引发生变化」的项做 FLIP；索引不变的卡片不施加位移动画。
        /// </summary>
        private void RunSelectiveFlipAnimation(Dictionary<int, Rect> before, Dictionary<int, int> oldIndexByKey)
        {
            for (int i = 0; i < _todos.Count; i++)
            {
                var item = _todos[i];
                int key = ItemSlideKey(item);
                if (!oldIndexByKey.TryGetValue(key, out int oldIdx) || oldIdx == i)
                    continue;
                if (!before.TryGetValue(key, out var oldRect))
                    continue;
                if (ToDoRepeater.TryGetElement(i) is not FrameworkElement fe)
                    continue;

                var newRect = fe.TransformToVisual(ToDoRepeater)
                    .TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                double dx = oldRect.X - newRect.X;
                double dy = oldRect.Y - newRect.Y;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
                    continue;

                StopFlipForKey(key);

                var tt = new TranslateTransform { X = dx, Y = dy };
                fe.RenderTransform = tt;
                fe.RenderTransformOrigin = new Point(0, 0);

                var sb = new Storyboard();
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = new Duration(TimeSpan.FromMilliseconds(FlipDurationMs));

                var animX = new DoubleAnimation
                {
                    From = dx,
                    To = 0,
                    Duration = dur,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(animX, fe);
                Storyboard.SetTargetProperty(animX, "(UIElement.RenderTransform).(TranslateTransform.X)");

                var animY = new DoubleAnimation
                {
                    From = dy,
                    To = 0,
                    Duration = dur,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(animY, fe);
                Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(TranslateTransform.Y)");

                sb.Children.Add(animX);
                sb.Children.Add(animY);

                void OnComplete()
                {
                    _flipStoryboards.Remove(key);
                    if (ReferenceEquals(fe.RenderTransform, tt))
                        fe.RenderTransform = null;
                }

                sb.Completed += (_, _) => OnComplete();
                _flipStoryboards[key] = sb;
                sb.Begin();
            }
        }

        private void StopFlipForKey(int key)
        {
            if (!_flipStoryboards.TryGetValue(key, out var sb))
                return;
            try
            {
                sb.Stop();
            }
            catch
            {
                // ignore
            }

            _flipStoryboards.Remove(key);
        }

        private void ScheduleMergeReveal(ToDoItem item)
        {
            _mergeRevealItem = item;

            if (_mergeRevealLayoutWaiter != null)
                ToDoRepeater.LayoutUpdated -= _mergeRevealLayoutWaiter;

            _mergeRevealLayoutWaiter = OnMergeRevealLayoutWaiter;
            ToDoRepeater.LayoutUpdated += _mergeRevealLayoutWaiter;
        }

        private void OnMergeRevealLayoutWaiter(object? sender, object e)
        {
            if (_mergeRevealLayoutWaiter != null)
                ToDoRepeater.LayoutUpdated -= _mergeRevealLayoutWaiter;
            _mergeRevealLayoutWaiter = null;

            var item = _mergeRevealItem;
            _mergeRevealItem = null;

            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (item == null || FindRepeaterItemRoot(item) is not FrameworkElement root)
                    return;

                root.RenderTransformOrigin = new Point(0.5, 0.5);
                var st = new ScaleTransform { ScaleX = 0.92, ScaleY = 0.92 };
                root.RenderTransform = st;
                root.Opacity = 0.65;

                var sb = new Storyboard();
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = new Duration(TimeSpan.FromMilliseconds(220));

                var opAnim = new DoubleAnimation
                {
                    From = 0.65,
                    To = 1,
                    Duration = dur,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(opAnim, root);
                Storyboard.SetTargetProperty(opAnim, "Opacity");

                var sxAnim = new DoubleAnimation
                {
                    From = 0.92,
                    To = 1,
                    Duration = dur,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(sxAnim, st);
                Storyboard.SetTargetProperty(sxAnim, "ScaleX");

                var syAnim = new DoubleAnimation
                {
                    From = 0.92,
                    To = 1,
                    Duration = dur,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(syAnim, st);
                Storyboard.SetTargetProperty(syAnim, "ScaleY");

                sb.Children.Add(opAnim);
                sb.Children.Add(sxAnim);
                sb.Children.Add(syAnim);
                sb.Completed += (_, _) =>
                {
                    root.RenderTransform = null;
                    root.ClearValue(UIElement.OpacityProperty);
                };
                sb.Begin();
            });
        }

        private FrameworkElement? FindRepeaterItemRoot(ToDoItem item)
        {
            for (int i = 0; i < _todos.Count; i++)
            {
                if (!ReferenceEquals(_todos[i], item))
                    continue;
                return ToDoRepeater.TryGetElement(i) as FrameworkElement;
            }

            return null;
        }

        private void ClearRepeaterSlideState()
        {
            foreach (var sb in _flipStoryboards.Values.ToList())
            {
                try
                {
                    sb.Stop();
                }
                catch
                {
                    // ignore
                }
            }

            _flipStoryboards.Clear();

            for (int i = 0; i < _todos.Count; i++)
            {
                if (ToDoRepeater.TryGetElement(i) is FrameworkElement fe)
                    fe.RenderTransform = null;
            }
        }

        private void MergePlaceholderAndItem()
        {
            if (_dragItem == null || _dragPlaceholder == null)
                return;

            int phIdx = _todos.IndexOf(_dragPlaceholder);
            if (phIdx < 0)
                return;

            _todos.RemoveAt(phIdx);
            _todos.Insert(phIdx, _dragItem);
            _dragItem = null;
            _dragPlaceholder = null;
        }

        private void EnsureDragGhost(ToDoItem item)
        {
            if (_dragGhost != null)
                return;

            double w = _dragGhostCellSize.Width > 1 ? _dragGhostCellSize.Width : 248;
            double h = _dragGhostCellSize.Height > 1 ? _dragGhostCellSize.Height : 184;

            var ghost = new Border
            {
                Width = w,
                Height = h,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10),
                Opacity = 0.98,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            if (ThemeResourceHelper.TryGetThemeBrush(this, "CourseListCardBackgroundBrush", out var bg) && bg != null)
                ghost.Background = bg;
            else
                ghost.Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

            if (ThemeResourceHelper.TryGetThemeBrush(this, "CourseListCardStrokeBrush", out var st) && st != null)
                ghost.BorderBrush = st;
            else
                ghost.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));

            ghost.Translation = new System.Numerics.Vector3(0, 0, 28);

            ThemeResourceHelper.TryGetThemeBrush(this, "TextFillColorSecondaryBrush", out var secondaryBrush);

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnSpacing = 10;

            var ghostCheck = new CheckBox
            {
                Content = "完成",
                IsChecked = item.Completed,
                IsEnabled = false,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.95
            };
            Grid.SetColumn(ghostCheck, 0);
            header.Children.Add(ghostCheck);

            var priority = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(6),
                MinWidth = 44,
                Background = new SolidColorBrush(item.PriorityEndColor)
            };
            priority.Child = new TextBlock
            {
                Text = item.PriorityDisplayName,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(priority, 1);
            header.Children.Add(priority);
            Grid.SetRow(header, 0);
            rootGrid.Children.Add(header);

            var titleTb = new TextBlock
            {
                Text = item.Title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Grid.SetRow(titleTb, 1);
            rootGrid.Children.Add(titleTb);

            var contentTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(item.Content) ? "\u200b" : item.Content,
                FontSize = 13,
                Foreground = secondaryBrush ?? new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(contentTb, 2);
            rootGrid.Children.Add(contentTb);

            var dueTb = new TextBlock
            {
                Text = item.DueDateText,
                FontSize = 12,
                Foreground = secondaryBrush ?? new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Bottom,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(dueTb, 3);
            rootGrid.Children.Add(dueTb);

            ghost.Child = rootGrid;
            _dragGhost = ghost;
            Canvas.SetZIndex(_dragGhost, 1000);
            DragOverlay.Children.Add(_dragGhost);
        }

        private void UpdateDragGhostPosition(PointerRoutedEventArgs e)
        {
            if (_dragGhost == null)
                return;

            var p = e.GetCurrentPoint(DragOverlay).Position;
            double w = _dragGhost.Width;
            double h = _dragGhost.Height;
            Canvas.SetLeft(_dragGhost, p.X - w * 0.5);
            Canvas.SetTop(_dragGhost, p.Y - h * 0.5);
        }

        private void RemoveDragGhost()
        {
            if (_dragGhost == null)
                return;
            DragOverlay.Children.Remove(_dragGhost);
            _dragGhost = null;
        }

        /// <summary>落点槽位 0..n。命中用扩展矩形吞掉列/行间距；最近格平局时优先更靠下、更靠右（贴合竖直下移）。</summary>
        private int ComputeDropSlotFromPoint(Point posInRepeater)
        {
            int n = _todos.Count;
            if (n == 0)
                return 0;

            var hits = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!TryGetRepeaterCellBoundsInflated(i, out var b))
                    continue;
                if (posInRepeater.X >= b.Left && posInRepeater.X <= b.Right &&
                    posInRepeater.Y >= b.Top && posInRepeater.Y <= b.Bottom)
                    hits.Add(i);
            }

            int pick;
            if (hits.Count == 1)
            {
                pick = hits[0];
            }
            else if (hits.Count > 1)
            {
                pick = hits[0];
                if (!TryGetRepeaterCellBounds(pick, out var bPick))
                    return n;
                double best = DistanceSquaredToCellCenter(posInRepeater, bPick);
                for (int k = 1; k < hits.Count; k++)
                {
                    int i = hits[k];
                    if (!TryGetRepeaterCellBounds(i, out var bb))
                        continue;
                    double d = DistanceSquaredToCellCenter(posInRepeater, bb);
                    if (d < best - 1e-3)
                    {
                        best = d;
                        pick = i;
                    }
                    else if (Math.Abs(d - best) < 1e-3)
                    {
                        if (bb.Top > bPick.Top + 0.5)
                        {
                            pick = i;
                            bPick = bb;
                            best = d;
                        }
                        else if (Math.Abs(bb.Top - bPick.Top) < 0.5 && bb.Left > bPick.Left + 0.5)
                        {
                            pick = i;
                            bPick = bb;
                            best = d;
                        }
                    }
                }
            }
            else
            {
                pick = -1;
                double bestDist = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (!TryGetRepeaterCellBoundsInflated(i, out var b))
                        continue;
                    double d = DistanceSquaredPointToRect(posInRepeater, b);
                    if (d < bestDist - 1e-4)
                    {
                        bestDist = d;
                        pick = i;
                    }
                    else if (pick >= 0 && Math.Abs(d - bestDist) < 1e-4)
                    {
                        if (TryGetRepeaterCellBounds(i, out var bi) && TryGetRepeaterCellBounds(pick, out var bp))
                        {
                            if (bi.Top > bp.Top + 0.5)
                                pick = i;
                            else if (Math.Abs(bi.Top - bp.Top) < 0.5 && bi.Left > bp.Left + 0.5)
                                pick = i;
                        }
                    }
                }

                if (pick < 0)
                    pick = 0;
            }

            if (!TryGetRepeaterCellBounds(pick, out var cell))
                return n;

            bool leftHalf = posInRepeater.X < cell.Left + cell.Width * 0.5;
            int slot = leftHalf ? pick : pick + 1;
            return Math.Clamp(slot, 0, n);
        }

        private bool TryGetRepeaterCellBoundsInflated(int index, out Rect bounds)
        {
            if (!TryGetRepeaterCellBounds(index, out var raw))
            {
                bounds = default;
                return false;
            }

            bounds = new Rect(
                raw.Left - HitInflateX,
                raw.Top - HitInflateYTop,
                raw.Width + 2 * HitInflateX,
                raw.Height + HitInflateYTop + HitInflateYBottom);
            return true;
        }

        private static double DistanceSquaredToCellCenter(Point p, Rect r)
        {
            double cx = r.Left + r.Width * 0.5;
            double cy = r.Top + r.Height * 0.5;
            double dx = p.X - cx;
            double dy = p.Y - cy;
            return dx * dx + dy * dy;
        }

        /// <summary>点到轴对齐矩形的最短距离平方（在矩形内为 0）。</summary>
        private static double DistanceSquaredPointToRect(Point p, Rect r)
        {
            double qx = Math.Clamp(p.X, r.Left, r.Right);
            double qy = Math.Clamp(p.Y, r.Top, r.Bottom);
            double dx = p.X - qx;
            double dy = p.Y - qy;
            return dx * dx + dy * dy;
        }

        private bool TryGetRepeaterCellBounds(int index, out Rect bounds)
        {
            bounds = default;
            if (ToDoRepeater.TryGetElement(index) is not FrameworkElement fe)
                return false;
            bounds = fe.TransformToVisual(ToDoRepeater)
                .TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            return bounds.Width > 0.5 && bounds.Height > 0.5;
        }

        private static Border? FindDescendantBorderWithTag(DependencyObject root, ToDoItem todo)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is Border br && ReferenceEquals(br.Tag, todo))
                    return br;
                var found = FindDescendantBorderWithTag(c, todo);
                if (found != null)
                    return found;
            }
            return null;
        }

        private Border? FindTodoCardBorder(ToDoItem todo)
        {
            for (int i = 0; i < _todos.Count; i++)
            {
                var el = ToDoRepeater.TryGetElement(i);
                if (el == null)
                    continue;
                var b = FindDescendantBorderWithTag(el, todo);
                if (b != null)
                    return b;
            }
            return null;
        }

        private void ClearSelectionVisual()
        {
            if (_selectedCard == null)
                return;

            ApplyTodoCardSelectedVisual(_selectedCard, selected: false);
            AnimateCardHoverMetrics(_selectedCard, 1.0, 0, 120);
            _selectedCard = null;
        }

        private static void ApplyTodoCardSelectedVisual(Border card, bool selected)
        {
            card.BorderThickness = new Thickness(1);

            if (selected)
            {
                if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var accObj) && accObj is Color accent)
                    card.Background = new SolidColorBrush(Color.FromArgb(76, accent.R, accent.G, accent.B));
                if (ThemeResourceHelper.TryGetThemeBrush(card, "CourseListCardStrokeBrush", out var st) && st != null)
                    card.BorderBrush = st;
            }
            else
            {
                ThemeResourceHelper.ApplyDefaultCardChrome(card, card);
            }
        }

        private void DeleteTodoBtn_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button b && Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var o) && o is Brush br)
                b.Foreground = br;
        }

        private void DeleteTodoBtn_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button b)
                b.ClearValue(Control.ForegroundProperty);
        }

        private void ShowTodoSelectionTip(FrameworkElement target, string subtitle)
        {
            TodoSelectionTeachingTip.Target = target;
            TodoSelectionTeachingTip.Subtitle = subtitle;
            TodoSelectionTeachingTip.XamlRoot = XamlRoot;
            TodoSelectionTeachingTip.IsOpen = true;
        }

        private static bool PointerEventOriginatedInCheckBox(PointerRoutedEventArgs e)
        {
            for (var o = e.OriginalSource as DependencyObject; o != null; o = VisualTreeHelper.GetParent(o))
            {
                if (o is CheckBox)
                    return true;
            }
            return false;
        }

        private void ToDoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is ToDoItem { IsDragPlaceholder: true })
                return;
            if (_pressActive && _pressTodo != null && ReferenceEquals(border.Tag, _pressTodo))
                return;
            double scale = ReferenceEquals(border, _selectedCard) ? 1.03 : 1.02;
            AnimateCardHoverMetrics(border, scale, HoverLiftY, 120);
        }

        private void ToDoCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is ToDoItem { IsDragPlaceholder: true })
                return;
            if (_pressActive && _pressTodo != null && ReferenceEquals(border.Tag, _pressTodo))
                return;
            double targetScale = ReferenceEquals(border, _selectedCard) ? 1.03 : 1.0;
            AnimateCardHoverMetrics(border, targetScale, 0, 140);
        }

        private const double HoverLiftY = -9;

        private static bool TryGetCardHoverTransforms(Border border, out ScaleTransform? scale, out TranslateTransform? translate)
        {
            scale = null;
            translate = null;
            if (border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                scale = tg.Children[0] as ScaleTransform;
                translate = tg.Children[1] as TranslateTransform;
                return scale != null && translate != null;
            }
            return false;
        }

        private static void AnimateCardHoverMetrics(Border border, double toScale, double toTranslateY, int durationMs)
        {
            if (!TryGetCardHoverTransforms(border, out var st, out var tt) || st == null || tt == null)
                return;

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));

            void Add(DependencyObject target, string prop, double to)
            {
                var anim = new DoubleAnimation { To = to, Duration = dur, EasingFunction = ease };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            }

            Add(st, "ScaleX", toScale);
            Add(st, "ScaleY", toScale);
            Add(tt, "Y", toTranslateY);
            sb.Begin();
        }
    }
}