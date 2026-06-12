using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskOverlay.Core.Models;

var cli = CliArguments.Parse(args);
if (cli.Positionals.Count == 0 || cli.Positionals[0] is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

try
{
    var connection = ResolveConnection(cli);
    if (cli.Positionals[0].Equals("config", StringComparison.OrdinalIgnoreCase))
    {
        WriteOutput(new
        {
            connection.Url,
            token = cli.Has("show-token") ? connection.Token : Mask(connection.Token),
            connection.SettingsPath
        }, cli);
        return 0;
    }

    using var client = new HttpClient
    {
        BaseAddress = new Uri(connection.Url.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(cli.GetInt("timeout", 15))
    };
    if (!string.IsNullOrWhiteSpace(connection.Token))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
    }

    var command = NormalizeCommand(cli.Positionals);
    var result = await ExecuteAsync(client, command, cli);
    if (!cli.Has("quiet"))
    {
        WriteOutput(result.Value, cli);
    }

    return result.Success ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static async Task<CommandResult> ExecuteAsync(HttpClient client, Command command, CliArguments cli)
{
    return command.Group switch
    {
        "health" => await SendAsync(client, HttpMethod.Get, "health"),
        "task" => await ExecuteTaskAsync(client, command.Action, command.Arguments, cli),
        "proposal" => await ExecuteProposalAsync(client, command.Action, command.Arguments, cli),
        "goal" => await ExecuteGoalAsync(client, command.Action, command.Arguments, cli),
        "plan" => await ExecutePlanAsync(client, command.Action, command.Arguments, cli),
        _ => throw new ArgumentException($"未知命令：{command.Group} {command.Action}".Trim())
    };
}

static async Task<CommandResult> ExecuteTaskAsync(HttpClient client, string action, IReadOnlyList<string> arguments, CliArguments cli)
{
    switch (action)
    {
        case "list":
            var filter = cli.Get("filter") ?? "all";
            var search = cli.Get("search") ?? string.Empty;
            return await SendAsync(client, HttpMethod.Get, $"api/tasks?filter={Uri.EscapeDataString(filter)}&search={Uri.EscapeDataString(search)}");
        case "show":
            return await SendAsync(client, HttpMethod.Get, $"api/tasks/{RequireSingleId(arguments)}");
        case "add":
            return await SendPayloadsAsync(client, HttpMethod.Post, "api/tasks", ReadTaskPayloads(cli, arguments, requireTitle: true));
        case "update":
            var updateId = RequireSingleId(arguments);
            var current = await SendAsync(client, HttpMethod.Get, $"api/tasks/{updateId}");
            EnsureSuccess(current);
            var task = JsonSerializer.Deserialize<TaskItem>(JsonSerializer.Serialize(current.Value, CliJson.Options), CliJson.Options)
                       ?? throw new InvalidDataException("无法读取任务详情。");
            ApplyTaskOptions(task, cli, arguments.Skip(1).ToList(), requireTitle: false);
            return await SendAsync(client, HttpMethod.Put, $"api/tasks/{updateId}", task);
        case "complete":
            return await ExecuteForIdsAsync(client, arguments, cli, "api/tasks/{0}/complete", HttpMethod.Post, new { completed = true }, isTask: true);
        case "reopen":
            return await ExecuteForIdsAsync(client, arguments, cli, "api/tasks/{0}/complete", HttpMethod.Post, new { completed = false }, isTask: true);
        case "delete":
            RequireConfirmation(cli, "删除任务");
            return await ExecuteForIdsAsync(client, arguments, cli, "api/tasks/{0}/delete", HttpMethod.Delete, null, isTask: true);
        default:
            throw new ArgumentException($"未知 task 子命令：{action}");
    }
}

static async Task<CommandResult> ExecuteProposalAsync(HttpClient client, string action, IReadOnlyList<string> arguments, CliArguments cli)
{
    switch (action)
    {
        case "list":
            return await SendAsync(client, HttpMethod.Get, "api/proposals");
        case "show":
            return await SendAsync(client, HttpMethod.Get, $"api/proposals/{RequireSingleId(arguments)}");
        case "add":
            return await SendPayloadsAsync(client, HttpMethod.Post, "api/proposals", ReadProposalPayloads(cli, arguments));
        case "confirm":
            return await ExecuteForIdsAsync(client, arguments, cli, "api/proposals/{0}/confirm", HttpMethod.Post, null, isTask: false);
        case "reject":
            if (cli.Has("all"))
            {
                RequireConfirmation(cli, "拒绝全部提案");
            }
            return await ExecuteForIdsAsync(client, arguments, cli, "api/proposals/{0}/reject", HttpMethod.Delete, null, isTask: false);
        default:
            throw new ArgumentException($"未知 proposal 子命令：{action}");
    }
}

static async Task<CommandResult> ExecuteGoalAsync(HttpClient client, string action, IReadOnlyList<string> arguments, CliArguments cli)
{
    switch (action)
    {
        case "list":
            var status = cli.Get("status");
            return await SendAsync(client, HttpMethod.Get, string.IsNullOrWhiteSpace(status)
                ? "api/goals"
                : $"api/goals?status={Uri.EscapeDataString(status)}");
        case "show":
            return await SendAsync(client, HttpMethod.Get, $"api/goals/{RequireSingleId(arguments)}");
        case "add":
            return await SendPayloadsAsync(client, HttpMethod.Post, "api/goals", ReadGoalPayloads(cli, arguments, requireTitle: true));
        case "update":
            var goalId = RequireSingleId(arguments);
            var current = await SendAsync(client, HttpMethod.Get, $"api/goals/{goalId}");
            EnsureSuccess(current);
            var goal = JsonSerializer.Deserialize<Goal>(JsonSerializer.Serialize(current.Value, CliJson.Options), CliJson.Options)
                       ?? throw new InvalidDataException("无法读取目标详情。");
            ApplyGoalOptions(goal, cli, arguments.Skip(1).ToList(), requireTitle: false);
            return await SendAsync(client, HttpMethod.Put, $"api/goals/{goalId}", goal);
        case "delete":
            RequireConfirmation(cli, "删除目标");
            return await SendAsync(client, HttpMethod.Delete, $"api/goals/{RequireSingleId(arguments)}");
        default:
            throw new ArgumentException($"未知 goal 子命令：{action}");
    }
}

static async Task<CommandResult> ExecutePlanAsync(HttpClient client, string action, IReadOnlyList<string> arguments, CliArguments cli)
{
    return action switch
    {
        "" or "tomorrow" => await SendAsync(client, HttpMethod.Post, "api/plans/tomorrow", ReadPlanningRequest(cli, arguments)),
        _ => throw new ArgumentException($"未知 plan 子命令：{action}")
    };
}

static async Task<CommandResult> ExecuteForIdsAsync(
    HttpClient client,
    IReadOnlyList<string> arguments,
    CliArguments cli,
    string pathTemplate,
    HttpMethod method,
    object? body,
    bool isTask)
{
    var ids = GetIds(arguments, cli);
    if (cli.Has("all"))
    {
        var list = await SendAsync(client, HttpMethod.Get, isTask
            ? $"api/tasks?filter={Uri.EscapeDataString(cli.Get("filter") ?? "all")}&search={Uri.EscapeDataString(cli.Get("search") ?? string.Empty)}"
            : "api/proposals");
        EnsureSuccess(list);
        ids = ExtractIds(list.Value);
    }

    if (ids.Count == 0)
    {
        throw new ArgumentException("至少需要一个 ID，或使用 --all。");
    }

    var results = new List<object?>();
    var success = true;
    foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var result = await SendAsync(client, method, string.Format(CultureInfo.InvariantCulture, pathTemplate, Uri.EscapeDataString(id)), body);
        success &= result.Success;
        results.Add(result.Value);
    }

    return new CommandResult(success, results);
}

static async Task<CommandResult> SendPayloadsAsync<T>(HttpClient client, HttpMethod method, string path, IReadOnlyList<T> payloads)
{
    var results = new List<object?>();
    var success = true;
    foreach (var payload in payloads)
    {
        var result = await SendAsync(client, method, path, payload);
        success &= result.Success;
        results.Add(result.Value);
    }

    return payloads.Count == 1 ? new CommandResult(success, results[0]) : new CommandResult(success, results);
}

static async Task<CommandResult> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
    {
        request.Content = JsonContent.Create(body, options: CliJson.Options);
    }

    using var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    object? value;
    try
    {
        value = JsonSerializer.Deserialize<JsonElement>(content, CliJson.Options);
    }
    catch (JsonException)
    {
        value = content;
    }

    return new CommandResult(response.IsSuccessStatusCode, value);
}

static IReadOnlyList<TaskItem> ReadTaskPayloads(CliArguments cli, IReadOnlyList<string> titleParts, bool requireTitle)
{
    var payloads = ReadJsonPayloads<TaskItem>(cli);
    if (payloads.Count > 0)
    {
        return payloads;
    }

    var task = new TaskItem();
    ApplyTaskOptions(task, cli, titleParts, requireTitle);
    return [task];
}

static IReadOnlyList<ExternalTaskProposal> ReadProposalPayloads(CliArguments cli, IReadOnlyList<string> titleParts)
{
    var payloads = ReadJsonPayloads<ExternalTaskProposal>(cli);
    if (payloads.Count > 0)
    {
        return payloads;
    }

    var task = new TaskItem();
    ApplyTaskOptions(task, cli, titleParts, requireTitle: true);
    return
    [
        new ExternalTaskProposal
        {
            Title = task.Title,
            Notes = task.Notes,
            Priority = task.Priority,
            DueAt = task.DueAt,
            ReminderAt = task.ReminderAt,
            IsDaily = task.IsDaily,
            Recurrence = task.Recurrence,
            Tags = task.Tags,
            Source = cli.Get("source") ?? "cli"
        }
    ];
}

static IReadOnlyList<Goal> ReadGoalPayloads(CliArguments cli, IReadOnlyList<string> titleParts, bool requireTitle)
{
    var payloads = ReadJsonPayloads<Goal>(cli);
    if (payloads.Count > 0)
    {
        return payloads;
    }

    var goal = new Goal();
    ApplyGoalOptions(goal, cli, titleParts, requireTitle);
    return [goal];
}

static void ApplyGoalOptions(Goal goal, CliArguments cli, IReadOnlyList<string> titleParts, bool requireTitle)
{
    if (titleParts.Count > 0)
    {
        goal.Title = string.Join(' ', titleParts);
    }
    else if (cli.Get("title") is { } title)
    {
        goal.Title = title;
    }
    else if (requireTitle && string.IsNullOrWhiteSpace(goal.Title))
    {
        throw new ArgumentException("需要目标标题，可直接写在命令后或使用 --title。");
    }

    if (cli.Get("description") is { } description) goal.Description = description;
    if (cli.Get("desc") is { } desc) goal.Description = desc;
    if (cli.Get("priority") is { } priority)
    {
        goal.Priority = Enum.TryParse<TaskPriority>(priority, true, out var parsed)
            ? parsed
            : throw new ArgumentException("--priority 必须是 low、normal、high 或 urgent。");
    }
    if (cli.Get("status") is { } status)
    {
        goal.Status = ParseGoalStatus(status);
    }
    if (cli.Get("horizon") is { } horizon)
    {
        goal.TimeHorizon = ParseGoalTimeHorizon(horizon);
    }
    var dailyMinutesText = cli.Get("daily-minutes") ?? cli.Get("daily-budget");
    if (dailyMinutesText is not null)
    {
        goal.DailyBudgetMinutes = int.TryParse(dailyMinutesText, out var minutes) && minutes > 0
            ? minutes
            : throw new ArgumentException("--daily-minutes 必须是正整数。");
    }

    var tagValues = cli.GetAll("tag")
        .Concat(cli.GetAll("tags").SelectMany(SplitValues))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (tagValues.Count > 0)
    {
        goal.Tags = tagValues.Select(name => new Tag { Name = name }).ToList();
    }

    if (cli.Get("milestone") is { } milestoneTitle)
    {
        goal.Milestones.Add(new GoalMilestone
        {
            Title = milestoneTitle,
            TargetDate = cli.Get("target") is { } target ? ParseDateOnly(target) : null,
            Status = MilestoneStatus.NotStarted
        });
    }
}

static GoalStatus ParseGoalStatus(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "active" or "进行中" or "启用" => GoalStatus.Active,
        "paused" or "pause" or "暂停" => GoalStatus.Paused,
        "completed" or "complete" or "done" or "完成" => GoalStatus.Completed,
        _ => throw new ArgumentException("--status 必须是 active、paused 或 completed。")
    };
}

static GoalTimeHorizon ParseGoalTimeHorizon(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "long-term" or "longterm" or "long" or "长期" => GoalTimeHorizon.LongTerm,
        "this-month" or "month" or "本月" => GoalTimeHorizon.ThisMonth,
        "this-week" or "week" or "本周" => GoalTimeHorizon.ThisWeek,
        _ => throw new ArgumentException("--horizon 必须是 long-term、this-month 或 this-week。")
    };
}

static DateOnly ParseDateOnly(string value)
{
    var dateTime = ParseFlexibleDateTime(value);
    return DateOnly.FromDateTime(dateTime);
}

static PlanningRequest ReadPlanningRequest(CliArguments cli, IReadOnlyList<string> arguments)
{
    var modeText = cli.Get("mode") ?? (arguments.Count > 0 ? arguments[0] : "task-list");
    var request = new PlanningRequest
    {
        Mode = modeText.ToLowerInvariant() switch
        {
            "task-list" or "tasklist" or "list" or "tasks" or "任务列表" => PlanningMode.TaskList,
            "time-block" or "timeblock" or "time" or "blocks" or "时间块" => PlanningMode.TimeBlock,
            _ => throw new ArgumentException("--mode 必须是 task-list 或 time-block。")
        },
        TargetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
        MaxItems = cli.GetInt("max", cli.GetInt("max-items", 8)),
        GoalSummary = cli.Get("goal")
    };

    foreach (var value in cli.GetAll("window").Concat(cli.GetAll("windows")).SelectMany(SplitValues))
    {
        var parts = value.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !TimeOnly.TryParse(parts[0], out var start) ||
            !TimeOnly.TryParse(parts[1], out var end) ||
            end <= start)
        {
            throw new ArgumentException($"时间段格式无效：{value}。示例：09:00-11:30");
        }

        request.TimeWindows.Add(new PlanningTimeWindow { Start = start, End = end });
    }

    return request;
}

static List<T> ReadJsonPayloads<T>(CliArguments cli)
{
    string? json = cli.Get("json");
    if (cli.Get("file") is { } file)
    {
        json = File.ReadAllText(Path.GetFullPath(file));
    }
    else if (cli.Has("stdin"))
    {
        json = Console.In.ReadToEnd();
    }

    if (string.IsNullOrWhiteSpace(json))
    {
        return [];
    }

    json = json.TrimStart('\uFEFF', '\u200B');
    using var document = JsonDocument.Parse(json);
    return document.RootElement.ValueKind == JsonValueKind.Array
        ? JsonSerializer.Deserialize<List<T>>(json, CliJson.Options) ?? []
        : [JsonSerializer.Deserialize<T>(json, CliJson.Options) ?? throw new InvalidDataException("JSON 内容不能为空。")];
}

static void ApplyTaskOptions(TaskItem task, CliArguments cli, IReadOnlyList<string> titleParts, bool requireTitle)
{
    if (titleParts.Count > 0)
    {
        task.Title = string.Join(' ', titleParts);
    }
    else if (cli.Get("title") is { } title)
    {
        task.Title = title;
    }
    else if (requireTitle && string.IsNullOrWhiteSpace(task.Title))
    {
        throw new ArgumentException("需要任务标题，可直接写在命令后或使用 --title。");
    }

    if (cli.Get("notes") is { } notes) task.Notes = notes;
    if (cli.Get("priority") is { } priority)
    {
        task.Priority = Enum.TryParse<TaskPriority>(priority, true, out var parsed)
            ? parsed
            : throw new ArgumentException("--priority 必须是 low、normal、high 或 urgent。");
    }
    if (cli.Get("due") is { } due) task.DueAt = ParseFlexibleDateTime(due);
    if (cli.Get("reminder") is { } reminder) task.ReminderAt = ParseFlexibleDateTime(reminder);

    var tagValues = cli.GetAll("tag")
        .Concat(cli.GetAll("tags").SelectMany(SplitValues))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (tagValues.Count > 0)
    {
        task.Tags = tagValues.Select(name => new Tag { Name = name }).ToList();
    }

    if (cli.Has("daily"))
    {
        task.IsDaily = true;
        task.Recurrence = null;
    }
    else if (cli.Get("repeat") is { } repeat)
    {
        (task.IsDaily, task.Recurrence) = ParseRecurrence(repeat, cli);
    }

    foreach (var field in cli.GetAll("clear").SelectMany(SplitValues))
    {
        switch (field.ToLowerInvariant())
        {
            case "notes": task.Notes = null; break;
            case "due": task.DueAt = null; break;
            case "reminder": task.ReminderAt = null; break;
            case "tags": task.Tags = []; break;
            case "repeat":
            case "recurrence": task.IsDaily = false; task.Recurrence = null; break;
            default: throw new ArgumentException($"不支持清空字段：{field}");
        }
    }
}

static (bool IsDaily, RecurrenceRule? Recurrence) ParseRecurrence(string value, CliArguments cli)
{
    var normalized = value.Trim().ToLowerInvariant();
    var interval = cli.GetInt("interval", 1);
    if (normalized is "daily" or "day" or "每天" && interval == 1)
    {
        return (true, null);
    }

    if (normalized.EndsWith('d') && int.TryParse(normalized[..^1], out var days))
    {
        return (false, new RecurrenceRule { Kind = RecurrenceKind.CustomDays, Interval = Math.Max(1, days) });
    }

    var kind = normalized switch
    {
        "daily" or "day" or "每天" => RecurrenceKind.Daily,
        "weekly" or "week" or "每周" => RecurrenceKind.Weekly,
        "monthly" or "month" or "每月" => RecurrenceKind.Monthly,
        _ => throw new ArgumentException("--repeat 支持 daily、weekly、monthly 或 3d。")
    };
    return (false, new RecurrenceRule
    {
        Kind = kind,
        Interval = Math.Max(1, interval),
        DayOfWeek = kind == RecurrenceKind.Weekly ? ParseDayOfWeek(cli.Get("day")) : null,
        DayOfMonth = kind == RecurrenceKind.Monthly ? cli.GetInt("day", DateTime.Today.Day) : null
    });
}

static DayOfWeek ParseDayOfWeek(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return DateTime.Today.DayOfWeek;
    if (Enum.TryParse<DayOfWeek>(value, true, out var parsed)) return parsed;
    return value.Trim() switch
    {
        "一" or "周一" or "星期一" => DayOfWeek.Monday,
        "二" or "周二" or "星期二" => DayOfWeek.Tuesday,
        "三" or "周三" or "星期三" => DayOfWeek.Wednesday,
        "四" or "周四" or "星期四" => DayOfWeek.Thursday,
        "五" or "周五" or "星期五" => DayOfWeek.Friday,
        "六" or "周六" or "星期六" => DayOfWeek.Saturday,
        "日" or "天" or "周日" or "星期日" or "周天" => DayOfWeek.Sunday,
        _ => throw new ArgumentException("--day 周几格式无效。")
    };
}

static DateTime ParseFlexibleDateTime(string value)
{
    var text = value.Trim();
    var lower = text.ToLowerInvariant();
    var now = DateTime.Now;
    if (lower is "now" or "现在") return now;
    if (TryParseRelative(lower, now, out var relative)) return relative;

    var dateAliases = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
    {
        ["today"] = DateTime.Today,
        ["今天"] = DateTime.Today,
        ["tomorrow"] = DateTime.Today.AddDays(1),
        ["明天"] = DateTime.Today.AddDays(1),
        ["后天"] = DateTime.Today.AddDays(2),
        ["yesterday"] = DateTime.Today.AddDays(-1),
        ["昨天"] = DateTime.Today.AddDays(-1)
    };
    foreach (var alias in dateAliases)
    {
        if (lower == alias.Key) return alias.Value;
        if (lower.StartsWith(alias.Key + " ", StringComparison.OrdinalIgnoreCase) &&
            TimeSpan.TryParse(text[(alias.Key.Length + 1)..], out var time))
        {
            return alias.Value.Add(time);
        }
    }

    return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
           DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)
        ? parsed
        : throw new ArgumentException($"时间格式无效：{value}");
}

static bool TryParseRelative(string value, DateTime now, out DateTime result)
{
    result = default;
    var text = value.StartsWith("in ", StringComparison.OrdinalIgnoreCase) ? value[3..].Trim() : value;
    if (!text.StartsWith('+') && !value.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    text = text.TrimStart('+');
    if (text.Length < 2 || !double.TryParse(text[..^1], CultureInfo.InvariantCulture, out var amount))
    {
        return false;
    }

    result = text[^1] switch
    {
        'm' => now.AddMinutes(amount),
        'h' => now.AddHours(amount),
        'd' => now.AddDays(amount),
        'w' => now.AddDays(amount * 7),
        _ => default
    };
    return result != default;
}

static Command NormalizeCommand(IReadOnlyList<string> positionals)
{
    var first = positionals[0].ToLowerInvariant();
    if (first is "health" or "status") return new Command("health", string.Empty, []);
    if (first is "tasks" or "ls") return new Command("task", "list", positionals.Skip(1).ToList());
    if (first is "proposals") return new Command("proposal", "list", positionals.Skip(1).ToList());
    if (first is "add") return new Command("proposal", "add", positionals.Skip(1).ToList());
    if (first is "confirm" or "reject") return new Command("proposal", first, positionals.Skip(1).ToList());
    if (first is "complete" or "reopen" or "delete") return new Command("task", first, positionals.Skip(1).ToList());
    if (first is "plan" or "planning")
    {
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : "tomorrow";
        return new Command("plan", action, positionals.Skip(2).ToList());
    }
    if (first is "goal" or "g")
    {
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : "list";
        if (action is "ls") action = "list";
        if (action is "get") action = "show";
        if (action is "rm") action = "delete";
        return new Command("goal", action, positionals.Skip(2).ToList());
    }
    if (first is "task" or "t" or "proposal" or "p")
    {
        var group = first is "t" ? "task" : first is "p" ? "proposal" : first;
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : "list";
        if (action is "ls") action = "list";
        if (action is "get") action = "show";
        if (action is "rm") action = group == "task" ? "delete" : "reject";
        return new Command(group, action, positionals.Skip(2).ToList());
    }

    throw new ArgumentException($"未知命令：{first}");
}

static Connection ResolveConnection(CliArguments cli)
{
    var explicitUrl = cli.Get("url") ?? Environment.GetEnvironmentVariable("TASKOVERLAY_URL");
    var explicitToken = cli.Get("token") ?? Environment.GetEnvironmentVariable("TASKOVERLAY_TOKEN");
    var settingsPath = FindSettingsPath(cli.Get("settings"));
    string? settingsToken = null;
    int? settingsPort = null;
    if (settingsPath is not null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (document.RootElement.TryGetProperty("apiToken", out var tokenElement)) settingsToken = tokenElement.GetString();
        if (document.RootElement.TryGetProperty("apiPort", out var portElement)) settingsPort = portElement.GetInt32();
    }

    return new Connection(
        explicitUrl ?? $"http://127.0.0.1:{settingsPort ?? 43127}/",
        explicitToken ?? settingsToken,
        settingsPath);
}

static string? FindSettingsPath(string? explicitPath)
{
    var candidates = new List<string?>();
    if (!string.IsNullOrWhiteSpace(explicitPath))
    {
        candidates.Add(Directory.Exists(explicitPath) ? Path.Combine(explicitPath, "settings.json") : explicitPath);
    }
    if (Environment.GetEnvironmentVariable("TASKOVERLAY_SETTINGS_DIR") is { } envDirectory)
    {
        candidates.Add(Path.Combine(envDirectory, "settings.json"));
    }
    candidates.Add(Path.Combine(Environment.CurrentDirectory, "data", "settings.json"));
    candidates.Add(Path.Combine(Environment.CurrentDirectory, "src", "TaskOverlay.App", "bin", "Release", "net8.0-windows", "data", "settings.json"));
    candidates.Add(Path.Combine(Environment.CurrentDirectory, "src", "TaskOverlay.App", "bin", "Debug", "net8.0-windows", "data", "settings.json"));
    return candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(path!)).FirstOrDefault(File.Exists);
}

static void WriteOutput(object? value, CliArguments cli)
{
    var format = (cli.Get("output") ?? "json").ToLowerInvariant();
    if (format == "ids")
    {
        Console.WriteLine(string.Join(Environment.NewLine, ExtractIds(value)));
        return;
    }
    if (format == "table")
    {
        WriteTable(value);
        return;
    }

    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(CliJson.Options)
    {
        WriteIndented = format != "compact"
    }));
}

static void WriteTable(object? value)
{
    var element = ToElement(value);
    var items = element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : [element];
    foreach (var item in items)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine(item.ToString());
            continue;
        }
        Console.WriteLine(string.Join(" | ", new[]
        {
            ReadProperty(item, "id"),
            ReadProperty(item, "title"),
            ReadProperty(item, "priority"),
            ReadProperty(item, "dueAt"),
            ReadProperty(item, "source")
        }.Where(text => !string.IsNullOrWhiteSpace(text))));
    }
}

static string ReadProperty(JsonElement element, string name)
    => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;

static List<string> ExtractIds(object? value)
{
    var ids = new List<string>();
    var element = ToElement(value);
    if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray()) ids.AddRange(ExtractIds(item));
    }
    else if (element.ValueKind == JsonValueKind.Object)
    {
        if (element.TryGetProperty("id", out var id)) ids.Add(id.ToString());
        else if (element.TryGetProperty("taskId", out var taskId)) ids.Add(taskId.ToString());
    }
    return ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
}

static JsonElement ToElement(object? value)
    => value is JsonElement element ? element : JsonSerializer.SerializeToElement(value, CliJson.Options);

static List<string> GetIds(IReadOnlyList<string> arguments, CliArguments cli)
    => arguments.Concat(cli.GetAll("ids")).SelectMany(SplitValues).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

static IEnumerable<string> SplitValues(string value)
    => value.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static string RequireSingleId(IReadOnlyList<string> arguments)
    => arguments.Count > 0 ? arguments[0] : throw new ArgumentException("该命令需要 ID。");

static void RequireConfirmation(CliArguments cli, string operation)
{
    if (!cli.Has("yes"))
    {
        throw new ArgumentException($"{operation}需要显式添加 --yes。");
    }
}

static void EnsureSuccess(CommandResult result)
{
    if (!result.Success)
    {
        throw new InvalidOperationException(JsonSerializer.Serialize(result.Value, CliJson.Options));
    }
}

static string? Mask(string? token)
    => string.IsNullOrWhiteSpace(token) ? null : token.Length <= 8 ? "********" : $"{token[..4]}...{token[^4..]}";

static void PrintHelp()
{
    Console.WriteLine("""
        TaskOverlay CLI

        分组命令：
          task list [--filter today|tomorrow|thisWeek|overdue|completed|all] [--search 文本]
          task show <ID>
          task add <标题> [任务字段]                 直接创建正式任务
          task update <ID> [任务字段] [--clear due,tags,repeat]
          task complete|reopen <ID...> [--ids 1,2] [--all --filter today]
          task delete <ID...> --yes

          proposal list
          proposal show <ID>
          proposal add <标题> [任务字段] [--source ai]
          proposal confirm|reject <ID...> [--all]   reject --all 需要 --yes

          goal list [--status active|paused|completed]
          goal show <ID>
          goal add <标题> [--description 文本] [--priority high] [--horizon long-term|this-month|this-week] [--daily-minutes 90] [--milestone 标题 --target 日期]
          goal update <ID> [目标字段]
          goal delete <ID> --yes

          plan tomorrow [--mode task-list|time-block] [--window 09:00-11:30] [--goal 文本]

          health | config [--show-token]

        任务字段：
          --title、--notes、--priority low|normal|high|urgent
          --due "tomorrow 18:00"、--reminder "+2h"
          --tag AI --tag 工作，或 --tags "AI,工作"
          --daily，或 --repeat daily|weekly|monthly|3d [--interval 2] [--day Monday|周一|15]

        灵活输入：
          --json '{...}'        单个 JSON 对象或对象数组
          --file tasks.json     从文件读取对象或数组
          --stdin               从标准输入读取 JSON
          支持 --key value、--key=value、短参数 -u -t -o -q -y
          plan 支持 --max、--window/--windows、--goal。
          Windows PowerShell 5 建议优先使用 --file/--stdin；内联 JSON 的双引号需要写成 \"。

        输出：
          --output json|compact|table|ids
          --quiet

        连接：
          --url/-u、--token/-t、--settings、--timeout
          也支持 TASKOVERLAY_URL、TASKOVERLAY_TOKEN、TASKOVERLAY_SETTINGS_DIR。

        兼容旧命令：
          tasks、proposals、add、confirm、reject、complete、reopen、delete。
        """);
}

sealed record Command(string Group, string Action, IReadOnlyList<string> Arguments);
sealed record CommandResult(bool Success, object? Value);
sealed record Connection(string Url, string? Token, string? SettingsPath);

sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positionals { get; } = [];

    public static CliArguments Parse(string[] args)
    {
        var parsed = new CliArguments();
        var positionalOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--")
            {
                positionalOnly = true;
                continue;
            }
            if (positionalOnly || !argument.StartsWith('-') || argument == "-")
            {
                parsed.Positionals.Add(argument);
                continue;
            }

            string key;
            string? value = null;
            if (argument.StartsWith("--"))
            {
                var pair = argument[2..].Split('=', 2);
                key = pair[0];
                if (pair.Length == 2) value = pair[1];
            }
            else
            {
                key = argument[1..] switch
                {
                    "u" => "url",
                    "t" => "token",
                    "o" => "output",
                    "q" => "quiet",
                    "y" => "yes",
                    "d" => "due",
                    "p" => "priority",
                    "g" => "tag",
                    "n" => "notes",
                    "s" => "source",
                    _ => throw new ArgumentException($"未知短参数：{argument}")
                };
            }

            var flagOnly = key is "quiet" or "yes" or "all" or "daily" or "stdin" or "show-token";
            if (!flagOnly && value is null && index + 1 < args.Length && !args[index + 1].StartsWith('-'))
            {
                value = args[++index];
            }
            parsed.Add(key, value ?? "true");
        }
        return parsed;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Get(string key) => _values.TryGetValue(key, out var values) ? values.LastOrDefault() : null;
    public IReadOnlyList<string> GetAll(string key) => _values.TryGetValue(key, out var values) ? values : [];
    public int GetInt(string key, int fallback)
        => Get(key) is { } value && int.TryParse(value, out var parsed) ? parsed : fallback;

    private void Add(string key, string value)
    {
        if (!_values.TryGetValue(key, out var values))
        {
            values = [];
            _values[key] = values;
        }
        values.Add(value);
    }
}

static class CliJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
