using System.Collections.ObjectModel;
using System.Globalization;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.App.ViewModels;

public sealed class TaskListViewModel : ObservableObject, IDisposable
{
    private readonly TaskApplicationService _tasks;
    private readonly SynchronizationContext? _synchronizationContext;
    private string _newTaskTitle = string.Empty;
    private string _searchText = string.Empty;
    private TaskItem? _editingTask;
    private string _editorTitle = string.Empty;
    private string _editorNotes = string.Empty;
    private TaskPriority _editorPriority = TaskPriority.Normal;
    private string _editorDueAtText = string.Empty;
    private string _editorReminderAtText = string.Empty;
    private string _editorTagsText = string.Empty;
    private RecurrenceKind _editorRecurrenceKind;
    private string _editorRecurrenceIntervalText = "1";
    private DayOfWeek _editorRecurrenceDayOfWeek = DateTime.Today.DayOfWeek;
    private string _editorRecurrenceDayOfMonthText = DateTime.Today.Day.ToString(CultureInfo.InvariantCulture);
    private bool _isDateView;
    private TaskFilter _filter = TaskFilter.Today;
    private string? _errorMessage;
    private DateTime? _selectedDate = DateTime.Today;
    private int _loadVersion;

    public ObservableCollection<TaskItem> Items { get; } = [];

    public IReadOnlyList<TaskPriority> Priorities { get; } = Enum.GetValues<TaskPriority>();

    public IReadOnlyList<RecurrenceKind> RecurrenceKinds { get; } = Enum.GetValues<RecurrenceKind>();

    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; } = Enum.GetValues<DayOfWeek>();

    public TaskListViewModel(TaskApplicationService tasks)
    {
        _tasks = tasks;
        _synchronizationContext = SynchronizationContext.Current;
        _tasks.TasksChanged += Tasks_OnTasksChanged;
    }

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set => SetProperty(ref _newTaskTitle, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public TaskItem? EditingTask
    {
        get => _editingTask;
        private set
        {
            if (SetProperty(ref _editingTask, value))
            {
                OnPropertyChanged(nameof(EditorHeading));
            }
        }
    }

    public string EditorHeading => EditingTask is null ? "新建任务" : "编辑任务";

    public string EditorTitle
    {
        get => _editorTitle;
        set => SetProperty(ref _editorTitle, value);
    }

    public string EditorNotes
    {
        get => _editorNotes;
        set => SetProperty(ref _editorNotes, value);
    }

    public TaskPriority EditorPriority
    {
        get => _editorPriority;
        set => SetProperty(ref _editorPriority, value);
    }

    public string EditorDueAtText
    {
        get => _editorDueAtText;
        set => SetProperty(ref _editorDueAtText, value);
    }

    public string EditorReminderAtText
    {
        get => _editorReminderAtText;
        set => SetProperty(ref _editorReminderAtText, value);
    }

    public string EditorTagsText
    {
        get => _editorTagsText;
        set => SetProperty(ref _editorTagsText, value);
    }

    public RecurrenceKind EditorRecurrenceKind
    {
        get => _editorRecurrenceKind;
        set => SetProperty(ref _editorRecurrenceKind, value);
    }

    public string EditorRecurrenceIntervalText
    {
        get => _editorRecurrenceIntervalText;
        set => SetProperty(ref _editorRecurrenceIntervalText, value);
    }

    public DayOfWeek EditorRecurrenceDayOfWeek
    {
        get => _editorRecurrenceDayOfWeek;
        set => SetProperty(ref _editorRecurrenceDayOfWeek, value);
    }

    public string EditorRecurrenceDayOfMonthText
    {
        get => _editorRecurrenceDayOfMonthText;
        set => SetProperty(ref _editorRecurrenceDayOfMonthText, value);
    }

    public bool IsDateView
    {
        get => _isDateView;
        set => SetProperty(ref _isDateView, value);
    }

    public TaskFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
            {
                _ = LoadDateAsync();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public async Task LoadAsync()
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        try
        {
            ErrorMessage = null;
            var tasks = await _tasks.GetTasksAsync(Filter, SearchText);
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                Replace(tasks);
            }
        }
        catch (Exception ex)
        {
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                ErrorMessage = $"任务加载失败：{ex.Message}";
            }
        }
    }

    public async Task LoadDateAsync()
    {
        if (SelectedDate is null)
        {
            return;
        }

        var loadVersion = Interlocked.Increment(ref _loadVersion);
        try
        {
            ErrorMessage = null;
            var tasks = await _tasks.GetTasksForDateAsync(DateOnly.FromDateTime(SelectedDate.Value));
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                Replace(tasks);
            }
        }
        catch (Exception ex)
        {
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                ErrorMessage = $"日历加载失败：{ex.Message}";
            }
        }
    }

    public Task ReloadCurrentViewAsync() => IsDateView ? LoadDateAsync() : LoadAsync();

    public async Task AddAsync(bool isDaily = false)
    {
        try
        {
            ErrorMessage = null;
            await _tasks.AddTaskAsync(NewTaskTitle, isDaily ? DateTime.Today : null, isDaily);
            NewTaskTitle = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"添加任务失败：{ex.Message}";
        }
    }

    public void BeginNew()
    {
        EditingTask = null;
        EditorTitle = string.Empty;
        EditorNotes = string.Empty;
        EditorPriority = TaskPriority.Normal;
        EditorDueAtText = string.Empty;
        EditorReminderAtText = string.Empty;
        EditorTagsText = string.Empty;
        EditorRecurrenceKind = RecurrenceKind.None;
        EditorRecurrenceIntervalText = "1";
        EditorRecurrenceDayOfWeek = DateTime.Today.DayOfWeek;
        EditorRecurrenceDayOfMonthText = DateTime.Today.Day.ToString(CultureInfo.InvariantCulture);
        ErrorMessage = null;
    }

    public void BeginEdit(TaskItem task)
    {
        EditingTask = task;
        EditorTitle = task.Title;
        EditorNotes = task.Notes ?? string.Empty;
        EditorPriority = task.Priority;
        EditorDueAtText = FormatDateTime(task.DueAt);
        EditorReminderAtText = FormatDateTime(task.ReminderAt);
        EditorTagsText = string.Join(", ", task.Tags.Select(t => t.Name));
        EditorRecurrenceKind = task.IsDaily ? RecurrenceKind.Daily : task.Recurrence?.Kind ?? RecurrenceKind.None;
        EditorRecurrenceIntervalText = (task.Recurrence?.Interval ?? 1).ToString(CultureInfo.InvariantCulture);
        EditorRecurrenceDayOfWeek = task.Recurrence?.DayOfWeek ?? task.DueAt?.DayOfWeek ?? DateTime.Today.DayOfWeek;
        EditorRecurrenceDayOfMonthText = (task.Recurrence?.DayOfMonth ?? task.DueAt?.Day ?? DateTime.Today.Day).ToString(CultureInfo.InvariantCulture);
        ErrorMessage = null;
    }

    public async Task SaveEditorAsync()
    {
        try
        {
            ErrorMessage = null;
            if (!TryParseOptionalDateTime(EditorDueAtText, "截止时间", out var dueAt) ||
                !TryParseOptionalDateTime(EditorReminderAtText, "提醒时间", out var reminderAt) ||
                !TryBuildRecurrence(out var isDaily, out var recurrence))
            {
                return;
            }

            var existing = EditingTask;
            var saved = await _tasks.SaveTaskAsync(new TaskItem
            {
                Id = existing?.Id ?? 0,
                Title = EditorTitle,
                Notes = string.IsNullOrWhiteSpace(EditorNotes) ? null : EditorNotes.Trim(),
                Priority = EditorPriority,
                DueAt = dueAt,
                ReminderAt = reminderAt,
                ReminderOffsetMinutes = existing is not null &&
                                        existing.DueAt == dueAt &&
                                        existing.ReminderAt == reminderAt
                    ? existing.ReminderOffsetMinutes
                    : null,
                IsCompleted = existing is { IsRecurring: false } && existing.IsCompleted,
                CompletedAt = existing is { IsRecurring: false } ? existing.CompletedAt : null,
                IsDaily = isDaily,
                SortOrder = existing?.SortOrder ?? 0,
                CreatedAt = existing?.CreatedAt ?? DateTime.Now,
                Recurrence = recurrence,
                Tags = ParseTags(EditorTagsText)
            });

            BeginEdit(saved);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存任务失败：{ex.Message}";
        }
    }

    public async Task ToggleCompletedAsync(TaskItem task)
    {
        try
        {
            if (task.IsRecurring)
            {
                await _tasks.SetOccurrenceCompletedAsync(task.Id, DateOnly.FromDateTime(DateTime.Today), !task.IsCompleted);
            }
            else
            {
                await _tasks.SetCompletedAsync(task.Id, !task.IsCompleted);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"更新任务失败：{ex.Message}";
        }
    }

    public async Task DeleteAsync(TaskItem task)
    {
        try
        {
            await _tasks.DeleteTaskAsync(task.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"删除任务失败：{ex.Message}";
        }
    }

    private void Replace(IEnumerable<TaskItem> tasks)
    {
        Items.Clear();
        foreach (var task in tasks)
        {
            Items.Add(task);
        }
    }

    public void Dispose()
    {
        _tasks.TasksChanged -= Tasks_OnTasksChanged;
    }

    private void Tasks_OnTasksChanged(object? sender, EventArgs e)
    {
        if (_synchronizationContext is null)
        {
            _ = ReloadCurrentViewAsync();
            return;
        }

        _synchronizationContext.Post(_ => _ = ReloadCurrentViewAsync(), null);
    }

    private bool TryParseOptionalDateTime(string input, string fieldName, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (DateTime.TryParse(input.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
            DateTime.TryParse(input.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            value = parsed;
            return true;
        }

        ErrorMessage = $"{fieldName}格式无效，请使用 yyyy-MM-dd HH:mm。";
        return false;
    }

    private bool TryBuildRecurrence(out bool isDaily, out RecurrenceRule? recurrence)
    {
        isDaily = false;
        recurrence = null;
        if (EditorRecurrenceKind == RecurrenceKind.None)
        {
            return true;
        }

        if (!int.TryParse(EditorRecurrenceIntervalText, out var interval) || interval < 1)
        {
            ErrorMessage = "重复间隔必须是大于 0 的整数。";
            return false;
        }

        if (EditorRecurrenceKind == RecurrenceKind.Daily && interval == 1)
        {
            isDaily = true;
            return true;
        }

        int? dayOfMonth = null;
        if (EditorRecurrenceKind == RecurrenceKind.Monthly)
        {
            if (!int.TryParse(EditorRecurrenceDayOfMonthText, out var parsedDay) || parsedDay is < 1 or > 31)
            {
                ErrorMessage = "每月日期必须是 1 到 31。";
                return false;
            }

            dayOfMonth = parsedDay;
        }

        recurrence = new RecurrenceRule
        {
            Kind = EditorRecurrenceKind,
            Interval = interval,
            DayOfWeek = EditorRecurrenceKind == RecurrenceKind.Weekly ? EditorRecurrenceDayOfWeek : null,
            DayOfMonth = dayOfMonth
        };
        return true;
    }

    private static string FormatDateTime(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    private static List<Tag> ParseTags(string input)
    {
        return TaskTagRules.Normalize(input
            .Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => new Tag { Name = name }));
    }
}
