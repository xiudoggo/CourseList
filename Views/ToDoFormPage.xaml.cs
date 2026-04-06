using CourseList.Helpers;
using CourseList.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace CourseList.Views
{
    public sealed partial class ToDoFormPage : Page
    {
        private int? _editingId;
        private bool _isLoadingForEdit;
        private bool _dueDateSyncedOnce;

        // Tag selection (multi-select, tags are persisted in todos.json tag library)
        private static readonly string[] DefaultPresetTags = ["study", "work", "life", "exercise", "urgent", "exam"];
        private static readonly HashSet<string> DefaultPresetTagSet = new(DefaultPresetTags, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<string> TagOptionsForUi { get; } = new();

        private readonly HashSet<string> _tagOptionsSet = new(StringComparer.OrdinalIgnoreCase);
        private bool _tagOptionsLoadedOnce;

        private bool _isUpdatingPresetSelection;

        private static string NormalizeTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return string.Empty;
            // Keep consistent casing to make dedupe + deterministic colors stable.
            return tag.Trim().ToLowerInvariant();
        }

        public ToDoFormPage()
        {
            this.InitializeComponent();
            // Enable normal {Binding ...} usage in XAML.
            this.DataContext = this;
            BuildPriorityComboItems();
            SelectPriorityCombo(ToDoPriority.Medium);
            // 新建模式会在 OnNavigatedTo 里初始化默认截止日期。
            Loaded += ToDoFormPage_Loaded;
        }

        private async Task EnsureTagOptionsLoadedAsync()
        {
            if (_tagOptionsLoadedOnce)
                return;

            _tagOptionsLoadedOnce = true;

            var fromJson = await ToDoDataHelper.LoadTagLibraryAsync();
            var normalizedJson = fromJson
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(NormalizeTag)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Order: default presets first (fixed order), then json-custom tags in file order.
            var ordered = new List<string>();
            foreach (var p in DefaultPresetTags)
            {
                var norm = NormalizeTag(p);
                if (!ordered.Contains(norm, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(norm);
            }

            foreach (var t in normalizedJson)
            {
                if (!ordered.Contains(t, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(t);
            }

            TagOptionsForUi.Clear();
            _tagOptionsSet.Clear();
            foreach (var t in ordered)
            {
                TagOptionsForUi.Add(t);
                _tagOptionsSet.Add(t);
            }

            // Persist back once to ensure presets exist in tag library too.
            var fromNormSet = new HashSet<string>(fromJson.Select(NormalizeTag), StringComparer.OrdinalIgnoreCase);
            bool missingAnyPreset = DefaultPresetTags.Any(p => !fromNormSet.Contains(NormalizeTag(p)));
            if (missingAnyPreset || fromNormSet.Count != _tagOptionsSet.Count)
                await ToDoDataHelper.SaveTagLibraryAsync(TagOptionsForUi);
        }

        private void ToDoFormPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Some environments can call code-behind before named elements are fully realized.
            // Guard against null to prevent runtime crash.
            if (_dueDateSyncedOnce)
                return;
            _dueDateSyncedOnce = true;
            SyncDueDatePickerEnabledState();
        }

        private void SyncDueDatePickerEnabledState()
        {
            if (HasDueDateCheckBox == null || DueDatePicker == null)
                return;
            bool hasDueDate = HasDueDateCheckBox.IsChecked == true;
            DueDatePicker.IsEnabled = hasDueDate;
            DueDatePicker.Visibility = hasDueDate ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HasDueDateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // 加载编辑数据时，由 LoadForEditAsync 末尾统一同步，避免重复触发 Sync。
            if (_isLoadingForEdit)
                return;
            SyncDueDatePickerEnabledState();
        }

        private void BuildPriorityComboItems()
        {
            PriorityComboBox.Items.Clear();
            foreach (ToDoPriority p in Enum.GetValues<ToDoPriority>())
            {
                PriorityComboBox.Items.Add(new ComboBoxItem
                {
                    Content = ToDoItem.GetPriorityDisplayName(p),
                    Tag = p
                });
            }
        }

        private void SelectPriorityCombo(ToDoPriority priority)
        {
            foreach (var o in PriorityComboBox.Items)
            {
                if (o is ComboBoxItem cbi && cbi.Tag is ToDoPriority tp && tp == priority)
                {
                    PriorityComboBox.SelectedItem = cbi;
                    return;
                }
            }

            PriorityComboBox.SelectedIndex = (int)ToDoPriority.Medium;
        }

        private static ToDoPriority GetSelectedPriorityFromCombo(ComboBox combo)
        {
            return combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is ToDoPriority tp
                ? tp
                : ToDoPriority.Medium;
        }

        public void ApplyCompactMode(bool isCompact)
        {
            FormRootGrid.Margin = isCompact ? new Thickness(12) : new Thickness(20);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is int id)
            {
                _editingId = id;
                var prevHitTestVisible = FormRootGrid.IsHitTestVisible;
                FormRootGrid.IsHitTestVisible = false;
                try
                {
                    await LoadForEditAsync(id);
                }
                finally
                {
                    FormRootGrid.IsHitTestVisible = prevHitTestVisible;
                }
            }
            else
            {
                _editingId = null;
                FormTitleText.Text = "新增待办";
                HasDueDateCheckBox.IsChecked = true;
                DueDatePicker.Date = DateTimeOffset.Now.Date;

                await EnsureTagOptionsLoadedAsync();
                ResetTagsUIForNew();
            }
        }

        private void ContentTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
                return;

            // 回车时按需增高，避免内容增加后输入区过于拥挤。
            double nextHeight = Math.Min(ContentTextBox.MaxHeight, ContentTextBox.ActualHeight + 28);
            if (nextHeight > ContentTextBox.Height)
                ContentTextBox.Height = nextHeight;
        }

        private async Task LoadForEditAsync(int id)
        {
            _isLoadingForEdit = true;
            try
            {
                await EnsureTagOptionsLoadedAsync();
                var todos = await ToDoDataHelper.LoadToDosAsync();
                var todo = todos.FirstOrDefault(t => t.Id == id);
                if (todo == null)
                    return;

                FormTitleText.Text = "修改待办";
                // FormRootGrid temporarily disables hit-test during async load,
                // so it's safe to overwrite text without disturbing caret.
                TitleTextBox.Text = todo.Title;
                ContentTextBox.Text = todo.Content;

                // 截止日期可选：关闭开关时保存为 null，且不强行覆盖 DatePicker 的值。
                HasDueDateCheckBox.IsChecked = todo.DueDate.HasValue;
                if (todo.DueDate.HasValue)
                    DueDatePicker.Date = new DateTimeOffset(todo.DueDate.Value.Date);
                SyncDueDatePickerEnabledState();

                SelectPriorityCombo(todo.Priority);

                LoadTagsUIFromTodo(todo.Tags);
                await EnsureTagOptionsContainSelectedTagsAsync();
                await Task.Yield(); // allow ItemsRepeater to create chip visuals
                ApplyPresetSelectionToVisuals();

                CompletedCheckBox.IsChecked = todo.Completed;
            }
            finally
            {
                _isLoadingForEdit = false;
            }
        }

        private void ResetTagsUIForNew()
        {
            _selectedTags.Clear();
            ApplyPresetSelectionToVisuals();
        }

        private void LoadTagsUIFromTodo(List<string>? tags)
        {
            _selectedTags.Clear();

            if (tags != null)
            {
                foreach (var t in tags)
                {
                    var norm = NormalizeTag(t);
                    if (string.IsNullOrWhiteSpace(norm))
                        continue;
                    _selectedTags.Add(norm);
                }
            }

            TagSuggestBox.Text = string.Empty;
        }

        private async Task EnsureTagOptionsContainSelectedTagsAsync()
        {
            bool changed = false;
            foreach (var norm in _selectedTags)
            {
                if (_tagOptionsSet.Contains(norm))
                    continue;
                TagOptionsForUi.Add(norm);
                _tagOptionsSet.Add(norm);
                changed = true;
            }

            if (changed)
                await ToDoDataHelper.SaveTagLibraryAsync(TagOptionsForUi);
        }

        private void ApplyPresetSelectionToVisuals()
        {
            if (_isUpdatingPresetSelection)
                return;

            if (PresetTagsRepeater == null)
                return;

            _isUpdatingPresetSelection = true;
            try
            {
                foreach (var cb in FindVisualDescendants<CheckBox>(PresetTagsRepeater))
                {
                    if (cb.Tag == null)
                        continue;
                    var norm = NormalizeTag(cb.Tag.ToString());
                    bool isSelected = _selectedTags.Contains(norm);
                    cb.IsChecked = isSelected;
                }
            }
            finally
            {
                _isUpdatingPresetSelection = false;
            }
        }

        private void PresetTagsRepeater_ElementPrepared(Microsoft.UI.Xaml.Controls.ItemsRepeater sender, Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs args)
        {
            // ItemsRepeater creates elements lazily; sync selection at creation time to ensure edit-mode tags render correctly.
            if (args.Element is not CheckBox cb)
                return;

            if (cb.Tag == null)
                return;

            var norm = NormalizeTag(cb.Tag.ToString());
            if (string.IsNullOrWhiteSpace(norm))
                return;

            _isUpdatingPresetSelection = true;
            try
            {
                cb.IsChecked = _selectedTags.Contains(norm);
            }
            finally
            {
                _isUpdatingPresetSelection = false;
            }
        }

        private IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t)
                    yield return t;
                foreach (var descendant in FindVisualDescendants<T>(child))
                    yield return descendant;
            }
        }

        private void PresetTagCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingForEdit || _isUpdatingPresetSelection)
                return;
            if (sender is not CheckBox cb || cb.Tag == null)
                return;

            var norm = NormalizeTag(cb.Tag.ToString());
            if (string.IsNullOrWhiteSpace(norm))
                return;

            bool isChecked = cb.IsChecked == true;
            if (isChecked)
                _selectedTags.Add(norm);
            else
                _selectedTags.Remove(norm);
        }

        private void TagSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            // When user presses Enter, allow both selecting an existing suggestion and creating a new tag.
            var chosen = args.ChosenSuggestion?.ToString();
            var query = args.QueryText;
            var raw = chosen ?? query;
            _ = AddTagFromInputAsync(raw);

            // Keep UI snappy for multi-add.
            sender.Text = string.Empty;
        }

        private void TagSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var raw = args.SelectedItem?.ToString();
            _ = AddTagFromInputAsync(raw);
            sender.Text = string.Empty;
        }

        private async Task AddTagFromInputAsync(string? raw)
        {
            var norm = NormalizeTag(raw);
            if (string.IsNullOrWhiteSpace(norm))
                return;

            await EnsureTagOptionsLoadedAsync();

            if (!_tagOptionsSet.Contains(norm))
            {
                TagOptionsForUi.Add(norm);
                _tagOptionsSet.Add(norm);
                await ToDoDataHelper.SaveTagLibraryAsync(TagOptionsForUi);
                await Task.Yield();
                if (PresetTagsRepeater != null)
                    PresetTagsRepeater.ItemsSource = TagOptionsForUi;
            }

            if (!_selectedTags.Contains(norm))
                _selectedTags.Add(norm);

            ApplyPresetSelectionToVisuals();
        }

        private async void PresetTagDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag == null)
                return;

            var norm = NormalizeTag(b.Tag.ToString());
            if (string.IsNullOrWhiteSpace(norm))
                return;

            // 当前表单不再勾选此标签。
            _selectedTags.Remove(norm);
            if (_tagOptionsSet.Contains(norm))
            {
                TagOptionsForUi.Remove(norm);
                _tagOptionsSet.Remove(norm);
                await ToDoDataHelper.SaveTagLibraryAsync(TagOptionsForUi);
                await Task.Yield();
                if (PresetTagsRepeater != null)
                    PresetTagsRepeater.ItemsSource = TagOptionsForUi;
            }
            ApplyPresetSelectionToVisuals();
        }

        private void PresetTagChip_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not DependencyObject root)
                return;
            var btn = FindVisualDescendants<Button>(root).FirstOrDefault(x => x.Tag != null);
            if (btn != null)
            {
                btn.Opacity = 1;
                btn.IsHitTestVisible = true;
            }
        }

        private void PresetTagChip_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not DependencyObject root)
                return;
            var btn = FindVisualDescendants<Button>(root).FirstOrDefault(x => x.Tag != null);
            if (btn != null)
            {
                btn.Opacity = 0;
                btn.IsHitTestVisible = false;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame?.CanGoBack == true)
                Frame.GoBack();
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveBtn.IsEnabled = false;
            SaveBtn.Content = "保存中...";
            SaveProgressRing.Visibility = Visibility.Visible;
            SaveProgressRing.IsActive = true;

            try
            {
                bool saved = await SaveAsync();
                if (saved && Frame?.CanGoBack == true)
                    Frame.GoBack();
            }
            finally
            {
                SaveProgressRing.IsActive = false;
                SaveProgressRing.Visibility = Visibility.Collapsed;
                SaveBtn.Content = "保存";
                SaveBtn.IsEnabled = true;
            }
        }

        private async Task<bool> SaveAsync()
        {
            var title = TitleTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                FlyoutBase.ShowAttachedFlyout(TitleTextBox);
                return false;
            }

            var todos = await ToDoDataHelper.LoadToDosAsync();
            var tags = _selectedTags
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var priority = GetSelectedPriorityFromCombo(PriorityComboBox);
            DateTime? dueDate = HasDueDateCheckBox.IsChecked == true
                ? DueDatePicker.Date.DateTime.Date
                : null;

            if (_editingId.HasValue)
            {
                var target = todos.FirstOrDefault(t => t.Id == _editingId.Value);
                if (target != null)
                {
                    target.Title = title;
                    target.Content = ContentTextBox.Text ?? string.Empty;
                    target.DueDate = dueDate;
                    target.Priority = priority;
                    target.Tags = tags;
                    target.Completed = CompletedCheckBox.IsChecked == true;
                }
            }
            else
            {
                todos.Add(new ToDoItem
                {
                    Id = ToDoDataHelper.GetNextId(todos),
                    Title = title,
                    Content = ContentTextBox.Text ?? string.Empty,
                    DueDate = dueDate,
                    Priority = priority,
                    Tags = tags,
                    Completed = CompletedCheckBox.IsChecked == true,
                });
            }

            await ToDoDataHelper.SaveToDosAsync(todos);
            return true;
        }
    }
}
