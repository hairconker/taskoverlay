using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TaskOverlay.App.Services;
using TaskOverlay.App.ViewModels;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.App.Views;

public partial class ManagementWindow : Window
{
    private TaskApplicationService _tasks;
    private TaskListViewModel _viewModel;
    private ITaskRepository _repository;
    private readonly LocalSettingsStore _settingsStore;
    private readonly ExternalTaskProposalStore _proposals;
    private readonly GoalApplicationService _goals;
    private readonly Func<Task<string?>> _applySettings;
    private readonly Action<double> _previewOverlayOpacity;
    private readonly Action _exitApplication;

    public ManagementWindow(
        TaskApplicationService tasks,
        ITaskRepository repository,
        LocalSettingsStore settingsStore,
        ExternalTaskProposalStore proposals,
        GoalApplicationService goals,
        Func<Task<string?>> applySettings,
        Action<double> previewOverlayOpacity,
        Action exitApplication)
    {
        InitializeComponent();
        _tasks = tasks;
        _repository = repository;
        _settingsStore = settingsStore;
        _proposals = proposals;
        _goals = goals;
        _applySettings = applySettings;
        _previewOverlayOpacity = previewOverlayOpacity;
        _exitApplication = exitApplication;
        _viewModel = new TaskListViewModel(tasks);
        DataContext = _viewModel;
        LoadSettings();
        ClearGoalEditor();
        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            await LoadProposalsAsync();
            await LoadGoalsAsync();
        };
        _proposals.ProposalsChanged += Proposals_OnProposalsChanged;
        _goals.GoalsChanged += Goals_OnGoalsChanged;
        Closed += (_, _) =>
        {
            _proposals.ProposalsChanged -= Proposals_OnProposalsChanged;
            _goals.GoalsChanged -= Goals_OnGoalsChanged;
            _viewModel.Dispose();
        };
    }

    public ObservableCollection<ExternalTaskProposal> ProposalItems { get; } = [];
    public ObservableCollection<PlanningItem> PlanningItems { get; } = [];
    public ObservableCollection<string> PlanningWarnings { get; } = [];
    public ObservableCollection<Goal> GoalItems { get; } = [];
    private PlanningReview? _lastPlanningReview;
    private Goal? _editingGoal;

    public void UpdateServices(TaskApplicationService tasks, ITaskRepository repository)
    {
        _viewModel.Dispose();
        _tasks = tasks;
        _repository = repository;
        _viewModel = new TaskListViewModel(tasks);
        DataContext = _viewModel;
        LoadSettings();
        _ = _viewModel.LoadAsync();
    }

    private async void AddTask_OnClick(object sender, RoutedEventArgs e) => await _viewModel.AddAsync();

    private async void AddDailyTask_OnClick(object sender, RoutedEventArgs e) => await _viewModel.AddAsync(isDaily: true);

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void TaskTabs_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.OriginalSource != TaskTabs)
        {
            return;
        }

        if (TaskTabs.SelectedItem == CalendarTab)
        {
            _viewModel.IsDateView = true;
            await _viewModel.LoadDateAsync();
        }
        else if (TaskTabs.SelectedItem == TasksTab)
        {
            _viewModel.IsDateView = false;
            await _viewModel.LoadAsync();
        }
        else if (TaskTabs.SelectedItem == GoalsTab)
        {
            await LoadGoalsAsync();
        }
    }

    private void NewDetails_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginNew();

    private async void SaveDetails_OnClick(object sender, RoutedEventArgs e) => await _viewModel.SaveEditorAsync();

    private void EditTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TaskItem task)
        {
            _viewModel.BeginEdit(task);
        }
    }

    private async void ToggleTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TaskItem task)
        {
            await _viewModel.ToggleCompletedAsync(task);
        }
    }

    private async void DeleteTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TaskItem task)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                $"确定删除“{task.Title}”吗？删除后只能通过备份恢复。",
                "确认删除任务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            await _viewModel.DeleteAsync(task);
        }
    }

    private async void ConfirmProposal_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExternalTaskProposal proposal)
        {
            await _proposals.ConfirmAsync(proposal.Id, _tasks);
        }
    }

    private async void RejectProposal_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExternalTaskProposal proposal)
        {
            await _proposals.RejectAsync(proposal.Id);
        }
    }

    private async void RefreshProposals_OnClick(object sender, RoutedEventArgs e) => await LoadProposalsAsync();

    private async void RefreshGoals_OnClick(object sender, RoutedEventArgs e) => await LoadGoalsAsync();

    private void NewGoal_OnClick(object sender, RoutedEventArgs e) => ClearGoalEditor();

    private async void SaveGoal_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildGoalFromEditor(out var goal))
        {
            return;
        }

        try
        {
            await _goals.SaveGoalAsync(goal);
            await LoadGoalsAsync();
            ClearGoalEditor();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存目标失败：{ex.Message}", "目标库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditGoal_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Goal goal)
        {
            BeginEditGoal(goal);
        }
    }

    private async void DeleteGoal_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Goal goal)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"确定删除长期目标“{goal.Title}”吗？删除后只能通过 goals.bak.json 恢复。",
            "确认删除目标",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _goals.DeleteGoalAsync(goal.Id);
        await LoadGoalsAsync();
        if (_editingGoal?.Id == goal.Id)
        {
            ClearGoalEditor();
        }
    }

    private async void GeneratePlan_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildPlanningRequest(out var request))
        {
            return;
        }

        try
        {
            var planning = new LocalPlanningService(_tasks, _goals);
            _lastPlanningReview = await planning.BuildTomorrowPlanAsync(request);
            PlanningItems.Clear();
            PlanningWarnings.Clear();
            foreach (var item in _lastPlanningReview.Items)
            {
                PlanningItems.Add(item);
            }
            foreach (var warning in _lastPlanningReview.Warnings)
            {
                PlanningWarnings.Add(warning);
            }

            PlanningSummaryText.Text = _lastPlanningReview.Summary;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"生成明日计划失败：{ex.Message}", "明日规划", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddPlanProposals_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lastPlanningReview is null)
        {
            System.Windows.MessageBox.Show(this, "请先生成明日计划。", "明日规划", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var proposedItems = FlattenPlanningItems(_lastPlanningReview.Items)
            .Where(item => item.Kind == PlanningItemKind.ProposedTask)
            .ToList();
        if (proposedItems.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "当前计划没有需要新增到提案箱的任务。", "明日规划", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var item in proposedItems)
        {
            await _proposals.AddAsync(new ExternalTaskProposal
            {
                Title = item.Title,
                Notes = BuildProposalNotes(item),
                Priority = item.Priority,
                DueAt = item.DueAt,
                ReminderAt = item.ReminderAt,
                Tags = item.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList(),
                Source = "planning"
            });
        }

        await LoadProposalsAsync();
        System.Windows.MessageBox.Show(this, $"已加入 {proposedItems.Count} 条外部提案，请到“外部提案”页确认。", "明日规划", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Proposals_OnProposalsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(async () => await LoadProposalsAsync());
    }

    private async Task LoadProposalsAsync()
    {
        var proposals = await _proposals.GetAllAsync();
        ProposalItems.Clear();
        foreach (var proposal in proposals)
        {
            ProposalItems.Add(proposal);
        }
    }

    private void Goals_OnGoalsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(async () => await LoadGoalsAsync());
    }

    private async Task LoadGoalsAsync()
    {
        var goals = await _goals.GetGoalsAsync();
        GoalItems.Clear();
        foreach (var goal in goals)
        {
            GoalItems.Add(goal);
        }
    }

    private void BeginEditGoal(Goal goal)
    {
        _editingGoal = goal;
        GoalTitleBox.Text = goal.Title;
        GoalDescriptionBox.Text = goal.Description ?? string.Empty;
        GoalPriorityBox.SelectedValue = goal.Priority;
        GoalStatusBox.SelectedValue = goal.Status;
        GoalHorizonBox.SelectedValue = goal.TimeHorizon;
        GoalDailyMinutesBox.Text = goal.DailyBudgetMinutes?.ToString() ?? string.Empty;
        GoalTagsBox.Text = goal.TagSummary;

        var milestone = goal.Milestones.FirstOrDefault(m => m.Status != MilestoneStatus.Completed)
            ?? goal.Milestones.FirstOrDefault();
        GoalMilestoneTitleBox.Text = milestone?.Title ?? string.Empty;
        GoalMilestoneTargetBox.SelectedDate = milestone?.TargetDate?.ToDateTime(TimeOnly.MinValue);
        GoalEditorModeText.Text = $"正在编辑：#{goal.Id}";
    }

    private void ClearGoalEditor()
    {
        _editingGoal = null;
        GoalTitleBox.Text = string.Empty;
        GoalDescriptionBox.Text = string.Empty;
        GoalPriorityBox.SelectedValue = TaskPriority.Normal;
        GoalStatusBox.SelectedValue = GoalStatus.Active;
        GoalHorizonBox.SelectedValue = GoalTimeHorizon.LongTerm;
        GoalDailyMinutesBox.Text = string.Empty;
        GoalTagsBox.Text = string.Empty;
        GoalMilestoneTitleBox.Text = string.Empty;
        GoalMilestoneTargetBox.SelectedDate = null;
        GoalEditorModeText.Text = "新增目标";
    }

    private bool TryBuildGoalFromEditor(out Goal goal)
    {
        goal = new Goal();
        var title = GoalTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            System.Windows.MessageBox.Show(this, "目标标题不能为空。", "目标库", MessageBoxButton.OK, MessageBoxImage.Warning);
            GoalTitleBox.Focus();
            return false;
        }

        int? dailyBudgetMinutes = null;
        if (!string.IsNullOrWhiteSpace(GoalDailyMinutesBox.Text))
        {
            if (!int.TryParse(GoalDailyMinutesBox.Text.Trim(), out var minutes) || minutes is < 1 or > 1440)
            {
                System.Windows.MessageBox.Show(this, "每日投入必须是 1 到 1440 的整数，或留空。", "目标库", MessageBoxButton.OK, MessageBoxImage.Warning);
                GoalDailyMinutesBox.Focus();
                return false;
            }

            dailyBudgetMinutes = minutes;
        }

        var now = DateTime.Now;
        goal.Id = _editingGoal?.Id ?? 0;
        goal.CreatedAt = _editingGoal?.CreatedAt ?? now;
        goal.UpdatedAt = now;
        goal.Title = title;
        goal.Description = string.IsNullOrWhiteSpace(GoalDescriptionBox.Text) ? null : GoalDescriptionBox.Text.Trim();
        goal.Priority = GoalPriorityBox.SelectedValue is TaskPriority priority ? priority : TaskPriority.Normal;
        goal.Status = GoalStatusBox.SelectedValue is GoalStatus status ? status : GoalStatus.Active;
        goal.TimeHorizon = GoalHorizonBox.SelectedValue is GoalTimeHorizon horizon ? horizon : GoalTimeHorizon.LongTerm;
        goal.DailyBudgetMinutes = dailyBudgetMinutes;
        goal.Tags = GoalTagsBox.Text
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => new Tag { Name = name })
            .ToList();
        goal.Milestones = BuildEditedMilestones(_editingGoal?.Milestones ?? []);
        goal.TaskLinks = _editingGoal?.TaskLinks.Select(CloneTaskLink).ToList() ?? [];
        return true;
    }

    private List<GoalMilestone> BuildEditedMilestones(IReadOnlyList<GoalMilestone> existingMilestones)
    {
        var milestones = existingMilestones.Select(CloneMilestone).ToList();
        var milestoneTitle = GoalMilestoneTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(milestoneTitle))
        {
            return milestones;
        }

        var targetDate = GoalMilestoneTargetBox.SelectedDate is DateTime selectedDate
            ? DateOnly.FromDateTime(selectedDate)
            : (DateOnly?)null;
        var now = DateTime.Now;
        var milestone = milestones.FirstOrDefault(m => m.Status != MilestoneStatus.Completed)
            ?? milestones.FirstOrDefault();
        if (milestone is null)
        {
            milestones.Add(new GoalMilestone
            {
                Title = milestoneTitle,
                TargetDate = targetDate,
                Status = MilestoneStatus.NotStarted,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            milestone.Title = milestoneTitle;
            milestone.TargetDate = targetDate;
            milestone.UpdatedAt = now;
        }

        return milestones;
    }

    private static GoalMilestone CloneMilestone(GoalMilestone milestone) => new()
    {
        Id = milestone.Id,
        GoalId = milestone.GoalId,
        Title = milestone.Title,
        TargetDate = milestone.TargetDate,
        Status = milestone.Status,
        CreatedAt = milestone.CreatedAt,
        UpdatedAt = milestone.UpdatedAt
    };

    private static GoalTaskLink CloneTaskLink(GoalTaskLink link) => new()
    {
        Id = link.Id,
        GoalId = link.GoalId,
        TaskId = link.TaskId,
        ProposalId = link.ProposalId,
        Note = link.Note,
        CreatedAt = link.CreatedAt
    };

    private bool TryBuildPlanningRequest(out PlanningRequest request)
    {
        request = new PlanningRequest
        {
            Mode = PlanningModeBox.SelectedValue is PlanningMode selectedMode ? selectedMode : PlanningMode.TaskList,
            TargetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            GoalSummary = string.IsNullOrWhiteSpace(PlanningGoalBox.Text) ? null : PlanningGoalBox.Text.Trim()
        };

        if (!int.TryParse(PlanningMaxItemsBox.Text.Trim(), out var maxItems) || maxItems is < 1 or > 30)
        {
            System.Windows.MessageBox.Show(this, "最多建议数量必须是 1 到 30 的整数。", "明日规划", MessageBoxButton.OK, MessageBoxImage.Warning);
            PlanningMaxItemsBox.Focus();
            return false;
        }
        request.MaxItems = maxItems;

        foreach (var window in PlanningWindowsBox.Text
                     .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = window.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !TimeOnly.TryParse(parts[0], out var start) ||
                !TimeOnly.TryParse(parts[1], out var end) ||
                end <= start)
            {
                System.Windows.MessageBox.Show(this, $"时间段格式无效：{window}\n示例：09:00-11:30", "明日规划", MessageBoxButton.OK, MessageBoxImage.Warning);
                PlanningWindowsBox.Focus();
                return false;
            }

            request.TimeWindows.Add(new PlanningTimeWindow { Start = start, End = end });
        }

        return true;
    }

    private static IEnumerable<PlanningItem> FlattenPlanningItems(IEnumerable<PlanningItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenPlanningItems(item.Children))
            {
                yield return child;
            }
        }
    }

    private static string BuildProposalNotes(PlanningItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            parts.Add(item.Notes);
        }
        if (!string.IsNullOrWhiteSpace(item.TimeBlock))
        {
            parts.Add($"建议时间：{item.TimeBlock}");
        }
        if (item.GoalId is not null && !string.IsNullOrWhiteSpace(item.GoalTitle))
        {
            parts.Add($"关联目标：#{item.GoalId} {item.GoalTitle}");
        }
        if (!string.IsNullOrWhiteSpace(item.Reason))
        {
            parts.Add($"规划理由：{item.Reason}");
        }
        return string.Join(Environment.NewLine, parts);
    }

    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var saveButton = sender as System.Windows.Controls.Button;
        if (saveButton is not null)
        {
            saveButton.IsEnabled = false;
        }

        try
        {
            await SaveSettingsAsync();
        }
        finally
        {
            if (saveButton is not null)
            {
                saveButton.IsEnabled = true;
            }
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (!TryValidateSettingsInput(out var storageBackend, out var mysqlPort, out var hotkey))
        {
            return;
        }

        var settings = _settingsStore.Current;
        settings.MySqlHost = HostBox.Text.Trim();
        settings.StorageBackend = storageBackend;
        settings.MySqlPort = mysqlPort;
        settings.MySqlDatabase = DatabaseBox.Text.Trim();
        settings.MySqlUser = UserBox.Text.Trim();
        settings.MySqlPassword = PasswordBox.Password;
        settings.Hotkey = hotkey;
        settings.OverlayOpacity = OpacitySlider.Value;
        settings.IsTopmost = TopmostBox.IsChecked == true;
        settings.StartWithWindows = StartupBox.IsChecked == true;
        settings.ApiEnabled = ApiEnabledBox.IsChecked == true;
        settings.ApiPort = int.Parse(ApiPortBox.Text.Trim());
        settings.ApiToken = ApiTokenBox.Text.Trim();
        _settingsStore.Save(settings);

        string? startupError = null;
        try
        {
            WindowsStartupService.Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            startupError = ex.Message;
        }

        var error = await _applySettings();
        var startupMessage = startupError is null ? string.Empty : $"\n开机自启更新失败：{startupError}";
        if (error is null)
        {
            var storageMessage = settings.StorageBackend == TaskStorageBackend.Json
                ? "当前使用本地 JSON 存储。"
                : "数据库连接正常。";
            System.Windows.MessageBox.Show(this, $"设置已保存，{storageMessage}透明度、置顶和有效快捷键设置已生效。{startupMessage}", "设置", MessageBoxButton.OK, startupError is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        else
        {
            System.Windows.MessageBox.Show(this, $"设置已保存，但数据库连接失败，当前仍使用本地 JSON：{error}{startupMessage}", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool TryValidateSettingsInput(out TaskStorageBackend storageBackend, out uint mysqlPort, out string hotkey)
    {
        storageBackend = StorageBackendBox.SelectedValue is TaskStorageBackend selectedBackend
            ? selectedBackend
            : TaskStorageBackend.Json;
        hotkey = HotkeyBox.Text.Trim();
        mysqlPort = 3306;

        if (!IsSupportedHotkey(hotkey))
        {
            System.Windows.MessageBox.Show(
                this,
                "快捷键格式无效。建议使用 Ctrl+`；也可使用 Ctrl/Alt/Shift/Win 加一个字母、数字、F1-F24，例如 Ctrl+Shift+Y。旧版 ~+1 仍可兼容，但在游戏中可能失效。",
                "快捷键无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            HotkeyBox.Focus();
            return false;
        }

        if (!uint.TryParse(PortBox.Text.Trim(), out mysqlPort) || mysqlPort is < 1 or > 65535)
        {
            System.Windows.MessageBox.Show(this, "MySQL 端口必须是 1 到 65535 的整数。", "端口无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            PortBox.Focus();
            return false;
        }

        if (!int.TryParse(ApiPortBox.Text.Trim(), out var apiPort) || apiPort is < 1024 or > 65535)
        {
            System.Windows.MessageBox.Show(this, "API 端口必须是 1024 到 65535 的整数。", "端口无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            ApiPortBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiTokenBox.Text))
        {
            System.Windows.MessageBox.Show(this, "API 令牌不能为空。", "API 设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            ApiTokenBox.Focus();
            return false;
        }

        if (storageBackend == TaskStorageBackend.MySql &&
            (string.IsNullOrWhiteSpace(HostBox.Text) ||
             string.IsNullOrWhiteSpace(DatabaseBox.Text) ||
             string.IsNullOrWhiteSpace(UserBox.Text)))
        {
            System.Windows.MessageBox.Show(this, "切换到 MySQL 前，请先填写主机、数据库和用户名。", "MySQL 设置不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static bool IsSupportedHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        if (hotkey.Equals("~+1", StringComparison.OrdinalIgnoreCase) ||
            hotkey.Equals("`+1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var hasModifier = false;
        var hasKey = false;
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "ALT":
                case "SHIFT":
                case "WIN":
                case "WINDOWS":
                    hasModifier = true;
                    break;
                default:
                    hasKey = IsSupportedHotkeyKey(part);
                    break;
            }
        }

        return hasModifier && hasKey;
    }

    private static bool IsSupportedHotkeyKey(string value)
    {
        if (value is "`" or "~" or "·" ||
            value.Equals("OEM3", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Length == 1 && char.IsLetterOrDigit(value[0]))
        {
            return true;
        }

        var normalized = value.ToUpperInvariant();
        return normalized.StartsWith('F') &&
               int.TryParse(normalized[1..], out var functionKey) &&
               functionKey is >= 1 and <= 24;
    }

    private async void ExportData_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDataTransferRepository(out var dataRepository))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 TaskOverlay 数据备份",
            Filter = "JSON 数据备份 (*.json)|*.json",
            FileName = $"TaskOverlay-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await dataRepository.ExportAsync(dialog.FileName);
            System.Windows.MessageBox.Show(this, $"数据已导出：\n{dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"导出失败：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportData_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDataTransferRepository(out var dataRepository))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入 TaskOverlay 数据备份",
            Filter = "JSON 数据备份 (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (System.Windows.MessageBox.Show(
                this,
                "导入会替换当前本地任务数据。系统会自动保留导入前的上一版本备份。是否继续？",
                "确认导入",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await dataRepository.ImportAsync(dialog.FileName);
            _tasks.NotifyTasksChanged();
            await _viewModel.ReloadCurrentViewAsync();
            System.Windows.MessageBox.Show(this, "数据导入完成，任务列表已刷新。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"导入失败，当前数据未被替换：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDataPath_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDataTransferRepository(out var dataRepository))
        {
            return;
        }

        System.Windows.Clipboard.SetText(dataRepository.DataFilePath);
        System.Windows.MessageBox.Show(this, "数据文件路径已复制。", "数据路径", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyDataFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDataTransferRepository(out var dataRepository))
        {
            return;
        }

        var directory = Path.GetDirectoryName(dataRepository.DataFilePath) ?? AppContext.BaseDirectory;
        System.Windows.Clipboard.SetText(directory);
        System.Windows.MessageBox.Show(this, "保存文件夹路径已复制。", "保存文件夹", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitApplication_OnClick(object sender, RoutedEventArgs e)
    {
        _exitApplication();
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Current;
        StorageBackendBox.SelectedValuePath = "Tag";
        StorageBackendBox.SelectedValue = settings.StorageBackend;
        HostBox.Text = settings.MySqlHost;
        PortBox.Text = settings.MySqlPort.ToString();
        DatabaseBox.Text = settings.MySqlDatabase;
        UserBox.Text = settings.MySqlUser;
        PasswordBox.Password = settings.MySqlPassword;
        HotkeyBox.Text = settings.Hotkey;
        OpacitySlider.Value = settings.OverlayOpacity;
        UpdateOpacityPreviewText(settings.OverlayOpacity);
        TopmostBox.IsChecked = settings.IsTopmost;
        StartupBox.IsChecked = settings.StartWithWindows;
        ApiEnabledBox.IsChecked = settings.ApiEnabled;
        ApiPortBox.Text = settings.ApiPort.ToString();
        ApiTokenBox.Text = settings.ApiToken;
        SettingsPathText.Text = _settingsStore.SettingsFilePath;
        DataPathText.Text = _repository is ITaskDataTransferRepository dataRepository
            ? dataRepository.DataFilePath
            : "当前使用 MySQL。切换到本地 JSON 后可使用文件导入与导出。";
    }

    private void CopyApiToken_OnClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(ApiTokenBox.Text);
        System.Windows.MessageBox.Show(this, "API 令牌已复制。", "本地 API", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOpacityPreviewText(e.NewValue);
        if (!IsLoaded)
        {
            return;
        }

        _previewOverlayOpacity(e.NewValue);
    }

    private void UpdateOpacityPreviewText(double value)
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = $"{Math.Round(value * 100)}%";
        }
    }

    private bool TryGetDataTransferRepository(out ITaskDataTransferRepository dataRepository)
    {
        if (_repository is ITaskDataTransferRepository available)
        {
            dataRepository = available;
            return true;
        }

        dataRepository = null!;
        System.Windows.MessageBox.Show(this, "当前使用 MySQL。请先切换到本地 JSON 并保存设置，再使用文件导入或导出。", "数据保护", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
}
