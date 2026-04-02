using CourseList.Helpers;
using CourseList.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace CourseList.Views
{
    public sealed partial class ToDoFormPage : Page
    {
        private int? _editingId;

        public ToDoFormPage()
        {
            this.InitializeComponent();
            BuildPriorityComboItems();
            SelectPriorityCombo(ToDoPriority.Medium);
            DueDatePicker.Date = DateTimeOffset.Now.Date;
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
                await LoadForEditAsync(id);
            }
            else
            {
                _editingId = null;
                FormTitleText.Text = "新增待办";
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
            var todos = await ToDoDataHelper.LoadToDosAsync();
            var todo = todos.FirstOrDefault(t => t.Id == id);
            if (todo == null)
                return;

            FormTitleText.Text = "修改待办";
            TitleTextBox.Text = todo.Title;
            ContentTextBox.Text = todo.Content;
            DueDatePicker.Date = todo.DueDate.HasValue ? new DateTimeOffset(todo.DueDate.Value.Date) : DateTimeOffset.Now.Date;
            SelectPriorityCombo(todo.Priority);
            TagsTextBox.Text = string.Join(",", todo.Tags ?? new List<string>());
            CompletedCheckBox.IsChecked = todo.Completed;
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
                await ContentDialogGuard.ShowAsync(new ContentDialog
                {
                    Title = "提示",
                    Content = "标题不能为空",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = this.ActualTheme
                });
                return false;
            }

            var todos = await ToDoDataHelper.LoadToDosAsync();
            var now = DateTime.Now;
            var tags = (TagsTextBox.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var priority = GetSelectedPriorityFromCombo(PriorityComboBox);
            var dueDate = DueDatePicker.Date.DateTime.Date;

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
                    target.UpdatedAt = now;
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
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await ToDoDataHelper.SaveToDosAsync(todos);
            return true;
        }
    }
}
