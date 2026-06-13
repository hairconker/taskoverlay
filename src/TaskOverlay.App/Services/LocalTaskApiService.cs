using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.App.Services;

public sealed class LocalTaskApiService(
    Func<TaskApplicationService> tasksProvider,
    ExternalTaskProposalStore proposals,
    Func<GoalApplicationService> goalsProvider,
    Action<string> overlayCommand,
    Func<AppSettings> settingsProvider) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public string BaseUrl => $"http://127.0.0.1:{settingsProvider().ApiPort}/";

    public void Start()
    {
        Stop();
        if (!settingsProvider().ApiEnabled)
        {
            return;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _cancellation = new CancellationTokenSource();
        _ = ListenAsync(_listener, _cancellation.Token);
    }

    public void Restart() => Start();

    public void Stop()
    {
        _cancellation?.Cancel();
        _listener?.Close();
        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task ListenAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = HandleAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (path == "/health")
            {
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { status = "ok", api = "TaskOverlay" }, cancellationToken);
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, HttpStatusCode.Unauthorized, new { error = "缺少或无效的 API 令牌。" }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/overlay/toggle-edit")
            {
                overlayCommand("toggle-edit");
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { overlay = "toggle-edit", accepted = true }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/overlay/show")
            {
                overlayCommand("show");
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { overlay = "show", accepted = true }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/overlay/hide")
            {
                overlayCommand("hide");
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { overlay = "hide", accepted = true }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/tasks")
            {
                var filterText = context.Request.QueryString["filter"];
                var filter = Enum.TryParse<TaskFilter>(filterText, true, out var parsed) ? parsed : TaskFilter.All;
                var tasks = await tasksProvider().GetTasksAsync(filter, context.Request.QueryString["search"], cancellationToken);
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, tasks, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/tasks")
            {
                var task = await ReadJsonAsync<TaskItem>(context.Request, cancellationToken);
                task.Id = 0;
                await WriteJsonAsync(context.Response, HttpStatusCode.Created, await tasksProvider().SaveTaskAsync(task, cancellationToken), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/proposals")
            {
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, await proposals.GetAllAsync(cancellationToken), cancellationToken);
                return;
            }

            if (path == "/api/plans/tomorrow" &&
                context.Request.HttpMethod is "GET" or "POST")
            {
                var request = context.Request.HttpMethod == "POST"
                    ? await ReadJsonAsync<PlanningRequest>(context.Request, cancellationToken)
                    : BuildPlanningRequestFromQuery(context.Request);
                request.TargetDate = request.TargetDate == default
                    ? DateOnly.FromDateTime(DateTime.Today.AddDays(1))
                    : request.TargetDate;
                var planning = new LocalPlanningService(tasksProvider(), goalsProvider());
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, await planning.BuildTomorrowPlanAsync(request, cancellationToken), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/goals")
            {
                var statusText = context.Request.QueryString["status"];
                var status = Enum.TryParse<GoalStatus>(statusText, true, out var parsedStatus) ? parsedStatus : (GoalStatus?)null;
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, await goalsProvider().GetGoalsAsync(status, cancellationToken), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/goals")
            {
                var goal = await ReadJsonAsync<Goal>(context.Request, cancellationToken);
                goal.Id = 0;
                await WriteJsonAsync(context.Response, HttpStatusCode.Created, await goalsProvider().SaveGoalAsync(goal, cancellationToken), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/proposals")
            {
                var proposal = await ReadJsonAsync<ExternalTaskProposal>(context.Request, cancellationToken);
                await WriteJsonAsync(context.Response, HttpStatusCode.Created, await proposals.AddAsync(proposal, cancellationToken), cancellationToken);
                return;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 4 && segments[0] == "api" && segments[1] == "proposals" && Guid.TryParse(segments[2], out var proposalId))
            {
                if (context.Request.HttpMethod == "POST" && segments[3] == "confirm")
                {
                    var task = await proposals.ConfirmAsync(proposalId, tasksProvider(), goalsProvider(), cancellationToken);
                    object result = task is null ? new { error = "提案不存在。" } : task;
                    await WriteJsonAsync(context.Response, task is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, result, cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod == "DELETE" && segments[3] == "reject")
                {
                    var removed = await proposals.RejectAsync(proposalId, cancellationToken);
                    await WriteJsonAsync(context.Response, removed ? HttpStatusCode.OK : HttpStatusCode.NotFound, new { removed }, cancellationToken);
                    return;
                }
            }

            if (segments.Length == 4 && segments[0] == "api" && segments[1] == "tasks" && long.TryParse(segments[2], out var taskId))
            {
                var taskExists = (await tasksProvider().GetTasksAsync(TaskFilter.All, cancellationToken: cancellationToken))
                    .Any(item => item.Id == taskId);
                if (!taskExists)
                {
                    await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "任务不存在。" }, cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod == "POST" && segments[3] == "complete")
                {
                    var request = await ReadJsonAsync<CompletionRequest>(context.Request, cancellationToken);
                    await tasksProvider().SetCompletedAsync(taskId, request.Completed, cancellationToken);
                    await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { taskId, request.Completed }, cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod == "DELETE" && segments[3] == "delete")
                {
                    await tasksProvider().DeleteTaskAsync(taskId, cancellationToken);
                    await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { taskId, deleted = true }, cancellationToken);
                    return;
                }
            }

            if (segments.Length == 3 && segments[0] == "api" && segments[1] == "tasks" && long.TryParse(segments[2], out taskId))
            {
                var task = (await tasksProvider().GetTasksAsync(TaskFilter.All, cancellationToken: cancellationToken))
                    .FirstOrDefault(item => item.Id == taskId);
                if (context.Request.HttpMethod == "GET")
                {
                    object result = task is null ? new { error = "任务不存在。" } : task;
                    await WriteJsonAsync(context.Response, task is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, result, cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod is "PUT" or "PATCH")
                {
                    if (task is null)
                    {
                        await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "任务不存在。" }, cancellationToken);
                        return;
                    }

                    var updated = await ReadJsonAsync<TaskItem>(context.Request, cancellationToken);
                    updated.Id = task.Id;
                    updated.CreatedAt = task.CreatedAt;
                    await WriteJsonAsync(context.Response, HttpStatusCode.OK, await tasksProvider().SaveTaskAsync(updated, cancellationToken), cancellationToken);
                    return;
                }
            }

            if (segments.Length == 3 && segments[0] == "api" && segments[1] == "proposals" && Guid.TryParse(segments[2], out proposalId))
            {
                var proposal = (await proposals.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == proposalId);
                object result = proposal is null ? new { error = "提案不存在。" } : proposal;
                await WriteJsonAsync(context.Response, proposal is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, result, cancellationToken);
                return;
            }

            if (segments.Length == 4 &&
                segments[0] == "api" &&
                segments[1] == "goals" &&
                long.TryParse(segments[2], out var linkGoalId) &&
                segments[3] == "links" &&
                context.Request.HttpMethod == "POST")
            {
                var request = await ReadJsonAsync<GoalTaskLinkRequest>(context.Request, cancellationToken);
                var taskExists = (await tasksProvider().GetTasksAsync(TaskFilter.All, cancellationToken: cancellationToken))
                    .Any(item => item.Id == request.TaskId);
                if (!taskExists)
                {
                    await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "任务不存在。" }, cancellationToken);
                    return;
                }

                var goal = await goalsProvider().LinkTaskAsync(linkGoalId, request.TaskId, note: request.Note, cancellationToken: cancellationToken);
                object result = goal is null ? new { error = "目标不存在。" } : goal;
                await WriteJsonAsync(context.Response, goal is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, result, cancellationToken);
                return;
            }

            if (segments.Length == 5 &&
                segments[0] == "api" &&
                segments[1] == "goals" &&
                long.TryParse(segments[2], out var unlinkGoalId) &&
                segments[3] == "links" &&
                long.TryParse(segments[4], out var linkId) &&
                context.Request.HttpMethod == "DELETE")
            {
                var removed = await goalsProvider().UnlinkTaskAsync(unlinkGoalId, linkId, cancellationToken);
                await WriteJsonAsync(context.Response, removed ? HttpStatusCode.OK : HttpStatusCode.NotFound, new { goalId = unlinkGoalId, linkId, removed }, cancellationToken);
                return;
            }

            if (segments.Length == 3 && segments[0] == "api" && segments[1] == "goals" && long.TryParse(segments[2], out var goalId))
            {
                if (context.Request.HttpMethod == "GET")
                {
                    var goal = await goalsProvider().GetGoalAsync(goalId, cancellationToken);
                    object result = goal is null ? new { error = "目标不存在。" } : goal;
                    await WriteJsonAsync(context.Response, goal is null ? HttpStatusCode.NotFound : HttpStatusCode.OK, result, cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod is "PUT" or "PATCH")
                {
                    var existing = await goalsProvider().GetGoalAsync(goalId, cancellationToken);
                    if (existing is null)
                    {
                        await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "目标不存在。" }, cancellationToken);
                        return;
                    }

                    var updated = await ReadJsonAsync<Goal>(context.Request, cancellationToken);
                    updated.Id = existing.Id;
                    updated.CreatedAt = existing.CreatedAt;
                    await WriteJsonAsync(context.Response, HttpStatusCode.OK, await goalsProvider().SaveGoalAsync(updated, cancellationToken), cancellationToken);
                    return;
                }

                if (context.Request.HttpMethod == "DELETE")
                {
                    var deleted = await goalsProvider().DeleteGoalAsync(goalId, cancellationToken);
                    await WriteJsonAsync(context.Response, deleted ? HttpStatusCode.OK : HttpStatusCode.NotFound, new { goalId, deleted }, cancellationToken);
                    return;
                }
            }

            await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "接口不存在。" }, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.BadRequest, new { error = ex.Message }, CancellationToken.None);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var expected = settingsProvider().ApiToken;
        var authorization = request.Headers["Authorization"];
        var supplied = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..].Trim()
            : request.Headers["X-TaskOverlay-Token"];
        return !string.IsNullOrWhiteSpace(expected) &&
               string.Equals(expected, supplied, StringComparison.Ordinal);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        var value = await JsonSerializer.DeserializeAsync<T>(request.InputStream, JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("请求 JSON 不能为空。");
    }

    private static PlanningRequest BuildPlanningRequestFromQuery(HttpListenerRequest request)
    {
        var modeText = request.QueryString["mode"];
        var mode = Enum.TryParse<PlanningMode>(modeText, true, out var parsedMode)
            ? parsedMode
            : PlanningMode.TaskList;
        var planningRequest = new PlanningRequest
        {
            Mode = mode,
            TargetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            GoalSummary = request.QueryString["goal"]
        };

        if (int.TryParse(request.QueryString["maxItems"], out var maxItems))
        {
            planningRequest.MaxItems = maxItems;
        }

        foreach (var window in (request.QueryString["windows"] ?? string.Empty)
                     .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = window.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                TimeOnly.TryParse(parts[0], out var start) &&
                TimeOnly.TryParse(parts[1], out var end))
            {
                planningRequest.TimeWindows.Add(new PlanningTimeWindow { Start = start, End = end });
            }
        }

        return planningRequest;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object value, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private sealed class CompletionRequest
    {
        public bool Completed { get; set; } = true;
    }

    private sealed class GoalTaskLinkRequest
    {
        public long TaskId { get; set; }
        public string? Note { get; set; }
    }
}
