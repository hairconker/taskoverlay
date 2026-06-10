using System.Text.Json;
using MySqlConnector;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.MySql;

public sealed class MySqlTaskRepository(Func<AppSettings> settingsProvider) : ITaskRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = settingsProvider();
        await EnsureDatabaseAsync(settings, cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);

        var sql = """
            CREATE TABLE IF NOT EXISTS tasks (
                id BIGINT PRIMARY KEY AUTO_INCREMENT,
                title VARCHAR(255) NOT NULL,
                notes TEXT NULL,
                priority INT NOT NULL DEFAULT 1,
                due_at DATETIME NULL,
                reminder_at DATETIME NULL,
                reminder_offset_minutes INT NULL,
                is_completed BOOLEAN NOT NULL DEFAULT FALSE,
                completed_at DATETIME NULL,
                is_daily BOOLEAN NOT NULL DEFAULT FALSE,
                sort_order INT NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tags (
                id BIGINT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(80) NOT NULL UNIQUE,
                color VARCHAR(16) NOT NULL DEFAULT '#6B7280'
            );
            CREATE TABLE IF NOT EXISTS task_tags (
                task_id BIGINT NOT NULL,
                tag_id BIGINT NOT NULL,
                PRIMARY KEY(task_id, tag_id)
            );
            CREATE TABLE IF NOT EXISTS recurrence_rules (
                id BIGINT PRIMARY KEY AUTO_INCREMENT,
                task_id BIGINT NOT NULL UNIQUE,
                kind INT NOT NULL,
                interval_value INT NOT NULL DEFAULT 1,
                day_of_week INT NULL,
                day_of_month INT NULL
            );
            CREATE TABLE IF NOT EXISTS reminders (
                id BIGINT PRIMARY KEY AUTO_INCREMENT,
                task_id BIGINT NOT NULL,
                remind_at DATETIME NOT NULL,
                delivered_at DATETIME NULL
            );
            CREATE TABLE IF NOT EXISTS daily_completions (
                id BIGINT PRIMARY KEY AUTO_INCREMENT,
                task_id BIGINT NOT NULL,
                completed_on DATE NOT NULL,
                completed_at DATETIME NOT NULL,
                UNIQUE KEY uq_daily_completion(task_id, completed_on)
            );
            CREATE TABLE IF NOT EXISTS app_settings (
                id INT PRIMARY KEY,
                settings_json JSON NOT NULL,
                updated_at DATETIME NOT NULL
            );
            """;

        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureReminderOffsetColumnAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter, string? search, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT id, title, notes, priority, due_at, reminder_at, reminder_offset_minutes, is_completed, completed_at, is_daily,
                   sort_order, created_at, updated_at
            FROM tasks
            ORDER BY is_completed ASC, sort_order ASC, COALESCE(due_at, '9999-12-31') ASC, created_at DESC;
            """, connection);

        var tasks = await ReadTasksAsync(command, cancellationToken);
        await LoadRecurrencesAsync(connection, tasks, cancellationToken);
        await LoadTagsAsync(connection, tasks, cancellationToken);

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        await ApplyOccurrenceCompletionStatusAsync(connection, tasks, today, cancellationToken);
        var query = tasks.AsEnumerable();
        query = filter switch
        {
            TaskFilter.Today => query.Where(t => !t.IsCompleted && (IsUndatedSingleTask(t) || TaskOccurrenceRules.OccursOn(t, today))),
            TaskFilter.Tomorrow => query.Where(t => !t.IsCompleted && TaskOccurrenceRules.OccursOn(t, today.AddDays(1))),
            TaskFilter.ThisWeek => query.Where(t => !t.IsCompleted && TaskOccurrenceRules.HasActiveOccurrenceBetween(t, today, today.AddDays(7))),
            TaskFilter.Overdue => query.Where(t => !TaskOccurrenceRules.IsRecurring(t) && !t.IsCompleted && t.DueAt is not null && t.DueAt.Value < now),
            TaskFilter.Completed => query.Where(t => t.IsCompleted),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     (t.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     t.Tags.Any(tag => tag.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT id, title, notes, priority, due_at, reminder_at, reminder_offset_minutes, is_completed, completed_at, is_daily,
                   sort_order, created_at, updated_at
            FROM tasks
            ORDER BY is_completed ASC, sort_order ASC, created_at DESC;
            """, connection);
        var tasks = await ReadTasksAsync(command, cancellationToken);
        await LoadRecurrencesAsync(connection, tasks, cancellationToken);
        await LoadTagsAsync(connection, tasks, cancellationToken);
        await ApplyOccurrenceCompletionStatusAsync(connection, tasks, date, cancellationToken);
        return tasks.Where(t => TaskOccurrenceRules.OccursOn(t, date))
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetDueRemindersAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT id, title, notes, priority, due_at, reminder_at, reminder_offset_minutes, is_completed, completed_at, is_daily,
                   sort_order, created_at, updated_at
            FROM tasks
            WHERE reminder_at IS NOT NULL
              AND reminder_at <= @now
            ORDER BY reminder_at ASC;
            """, connection);
        command.Parameters.AddWithValue("@now", now);
        var tasks = await ReadTasksAsync(command, cancellationToken);
        await LoadRecurrencesAsync(connection, tasks, cancellationToken);
        await LoadTagsAsync(connection, tasks, cancellationToken);
        await ApplyOccurrenceCompletionStatusAsync(connection, tasks, DateOnly.FromDateTime(now), cancellationToken);
        return tasks.Where(t => !t.IsCompleted).ToList();
    }

    public async Task MarkReminderDeliveredAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var read = new MySqlCommand("""
            SELECT id, title, notes, priority, due_at, reminder_at, reminder_offset_minutes, is_completed, completed_at, is_daily,
                   sort_order, created_at, updated_at
            FROM tasks
            WHERE id = @id;
            """, connection);
        read.Parameters.AddWithValue("@id", taskId);
        var tasks = await ReadTasksAsync(read, cancellationToken);
        if (tasks.Count == 0 || tasks[0].ReminderAt is null)
        {
            return;
        }

        await LoadRecurrencesAsync(connection, tasks, cancellationToken);
        TaskReminderRules.NormalizeOffset(tasks[0]);
        var nextReminderAt = TaskReminderRules.GetNextReminderAt(tasks[0], DateTime.Now);

        await using var command = new MySqlCommand("""
            UPDATE tasks
            SET reminder_at = @reminder_at, reminder_offset_minutes = @reminder_offset_minutes, updated_at = @updated_at
            WHERE id = @id AND reminder_at IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("@id", taskId);
        command.Parameters.AddWithValue("@reminder_at", (object?)nextReminderAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@reminder_offset_minutes", (object?)tasks[0].ReminderOffsetMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("@updated_at", DateTime.Now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TaskItem> SaveTaskAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        task.Tags = TaskTagRules.Normalize(task.Tags);
        TaskReminderRules.NormalizeOffset(task);
        if (TaskOccurrenceRules.IsRecurring(task))
        {
            task.IsCompleted = false;
            task.CompletedAt = null;
        }

        if (task.Id == 0)
        {
            await using var command = new MySqlCommand("""
                INSERT INTO tasks (title, notes, priority, due_at, reminder_at, reminder_offset_minutes, is_completed, completed_at, is_daily, sort_order, created_at, updated_at)
                VALUES (@title, @notes, @priority, @due_at, @reminder_at, @reminder_offset_minutes, @is_completed, @completed_at, @is_daily, @sort_order, @created_at, @updated_at);
                SELECT LAST_INSERT_ID();
                """, connection);
            AddTaskParameters(command, task);
            task.Id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        else
        {
            await using var command = new MySqlCommand("""
                UPDATE tasks
                SET title = @title, notes = @notes, priority = @priority, due_at = @due_at, reminder_at = @reminder_at,
                    reminder_offset_minutes = @reminder_offset_minutes,
                    is_completed = @is_completed, completed_at = @completed_at, is_daily = @is_daily,
                    sort_order = @sort_order, updated_at = @updated_at
                WHERE id = @id;
                """, connection);
            command.Parameters.AddWithValue("@id", task.Id);
            AddTaskParameters(command, task);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SaveRecurrenceAsync(connection, task, cancellationToken);
        await SaveTagsAsync(connection, task, cancellationToken);
        return task;
    }

    public async Task DeleteTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            DELETE FROM task_tags WHERE task_id = @id;
            DELETE FROM recurrence_rules WHERE task_id = @id;
            DELETE FROM reminders WHERE task_id = @id;
            DELETE FROM daily_completions WHERE task_id = @id;
            DELETE FROM tasks WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("@id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetCompletedAsync(long taskId, bool completed, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var recurrenceCommand = new MySqlCommand("""
            SELECT EXISTS(
                SELECT 1
                FROM tasks
                WHERE id = @id
                  AND (is_daily = TRUE OR EXISTS (
                      SELECT 1 FROM recurrence_rules rr WHERE rr.task_id = tasks.id AND rr.kind <> 0
                  ))
            );
            """, connection);
        recurrenceCommand.Parameters.AddWithValue("@id", taskId);
        if (Convert.ToBoolean(await recurrenceCommand.ExecuteScalarAsync(cancellationToken)))
        {
            await SetOccurrenceCompletedAsync(taskId, DateOnly.FromDateTime(DateTime.Today), completed, cancellationToken);
            return;
        }

        await using var command = new MySqlCommand("""
            UPDATE tasks
            SET is_completed = @completed, completed_at = @completed_at, updated_at = @updated_at
            WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("@id", taskId);
        command.Parameters.AddWithValue("@completed", completed);
        command.Parameters.AddWithValue("@completed_at", completed ? DateTime.Now : DBNull.Value);
        command.Parameters.AddWithValue("@updated_at", DateTime.Now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetOccurrenceCompletedAsync(long taskId, DateOnly date, bool completed, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(completed
            ? """
              INSERT INTO daily_completions (task_id, completed_on, completed_at)
              SELECT @task_id, @completed_on, @completed_at
              FROM tasks
              WHERE id = @task_id AND (
                  is_daily = TRUE OR EXISTS (
                      SELECT 1 FROM recurrence_rules rr WHERE rr.task_id = tasks.id AND rr.kind <> 0
                  )
              )
              ON DUPLICATE KEY UPDATE completed_at = VALUES(completed_at);
              UPDATE tasks SET is_completed = FALSE, completed_at = NULL, updated_at = @completed_at
              WHERE id = @task_id;
              """
            : """
              DELETE dc
              FROM daily_completions dc
              WHERE dc.task_id = @task_id AND dc.completed_on = @completed_on;
              UPDATE tasks SET is_completed = FALSE, completed_at = NULL, updated_at = @completed_at
              WHERE id = @task_id;
              """, connection);
        command.Parameters.AddWithValue("@task_id", taskId);
        command.Parameters.AddWithValue("@completed_on", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("@completed_at", DateTime.Now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT settings_json FROM app_settings WHERE id = 1;", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value == DBNull.Value)
        {
            return settingsProvider();
        }

        return JsonSerializer.Deserialize<AppSettings>(value.ToString()!, JsonOptions) ?? settingsProvider();
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            INSERT INTO app_settings (id, settings_json, updated_at)
            VALUES (1, @settings, @updated_at)
            ON DUPLICATE KEY UPDATE settings_json = VALUES(settings_json), updated_at = VALUES(updated_at);
            """, connection);
        command.Parameters.AddWithValue("@settings", JsonSerializer.Serialize(settings, JsonOptions));
        command.Parameters.AddWithValue("@updated_at", DateTime.Now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var settings = settingsProvider();
        var builder = BuildConnectionString(settings, includeDatabase: true);
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureDatabaseAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var builder = BuildConnectionString(settings, includeDatabase: false);
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{settings.MySqlDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MySqlConnectionStringBuilder BuildConnectionString(AppSettings settings, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.MySqlHost,
            Port = settings.MySqlPort,
            UserID = settings.MySqlUser,
            Password = settings.MySqlPassword,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true,
            ConnectionTimeout = 5
        };

        if (includeDatabase)
        {
            builder.Database = settings.MySqlDatabase;
        }

        return builder;
    }

    private static void AddTaskParameters(MySqlCommand command, TaskItem task)
    {
        command.Parameters.AddWithValue("@title", task.Title);
        command.Parameters.AddWithValue("@notes", (object?)task.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@priority", (int)task.Priority);
        command.Parameters.AddWithValue("@due_at", (object?)task.DueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@reminder_at", (object?)task.ReminderAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@reminder_offset_minutes", (object?)task.ReminderOffsetMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("@is_completed", task.IsCompleted);
        command.Parameters.AddWithValue("@completed_at", (object?)task.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@is_daily", task.IsDaily);
        command.Parameters.AddWithValue("@sort_order", task.SortOrder);
        command.Parameters.AddWithValue("@created_at", task.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", task.UpdatedAt);
    }

    private static async Task<IReadOnlyList<TaskItem>> ReadTasksAsync(MySqlCommand command, CancellationToken cancellationToken)
    {
        var tasks = new List<TaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(new TaskItem
            {
                Id = reader.GetInt64("id"),
                Title = reader.GetString("title"),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString("notes"),
                Priority = (TaskPriority)reader.GetInt32("priority"),
                DueAt = reader.IsDBNull(reader.GetOrdinal("due_at")) ? null : reader.GetDateTime("due_at"),
                ReminderAt = reader.IsDBNull(reader.GetOrdinal("reminder_at")) ? null : reader.GetDateTime("reminder_at"),
                ReminderOffsetMinutes = reader.IsDBNull(reader.GetOrdinal("reminder_offset_minutes")) ? null : reader.GetInt32("reminder_offset_minutes"),
                IsCompleted = reader.GetBoolean("is_completed"),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetDateTime("completed_at"),
                IsDaily = reader.GetBoolean("is_daily"),
                SortOrder = reader.GetInt32("sort_order"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            });
        }

        return tasks;
    }

    private static async Task ApplyOccurrenceCompletionStatusAsync(
        MySqlConnection connection,
        IReadOnlyList<TaskItem> tasks,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var recurringTasks = tasks.Where(TaskOccurrenceRules.IsRecurring).ToList();
        if (recurringTasks.Count == 0)
        {
            return;
        }

        await using var command = new MySqlCommand("""
            SELECT task_id, completed_at
            FROM daily_completions
            WHERE completed_on = @completed_on;
            """, connection);
        command.Parameters.AddWithValue("@completed_on", date.ToString("yyyy-MM-dd"));

        var completed = new Dictionary<long, DateTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            completed[reader.GetInt64("task_id")] = reader.GetDateTime("completed_at");
        }

        foreach (var task in recurringTasks)
        {
            task.IsCompleted = completed.TryGetValue(task.Id, out var completedAt);
            task.CompletedAt = task.IsCompleted ? completedAt : null;
        }
    }

    private static async Task LoadRecurrencesAsync(
        MySqlConnection connection,
        IReadOnlyList<TaskItem> tasks,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        await using var command = new MySqlCommand("""
            SELECT id, task_id, kind, interval_value, day_of_week, day_of_month
            FROM recurrence_rules;
            """, connection);
        var taskById = tasks.ToDictionary(t => t.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var taskId = reader.GetInt64("task_id");
            if (!taskById.TryGetValue(taskId, out var task))
            {
                continue;
            }

            task.Recurrence = new RecurrenceRule
            {
                Id = reader.GetInt64("id"),
                TaskId = taskId,
                Kind = (RecurrenceKind)reader.GetInt32("kind"),
                Interval = reader.GetInt32("interval_value"),
                DayOfWeek = reader.IsDBNull(reader.GetOrdinal("day_of_week")) ? null : (DayOfWeek)reader.GetInt32("day_of_week"),
                DayOfMonth = reader.IsDBNull(reader.GetOrdinal("day_of_month")) ? null : reader.GetInt32("day_of_month")
            };
        }
    }

    private static async Task SaveRecurrenceAsync(MySqlConnection connection, TaskItem task, CancellationToken cancellationToken)
    {
        await using var delete = new MySqlCommand("DELETE FROM recurrence_rules WHERE task_id = @task_id;", connection);
        delete.Parameters.AddWithValue("@task_id", task.Id);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        if (task.Recurrence is null || task.Recurrence.Kind == RecurrenceKind.None)
        {
            return;
        }

        await using var insert = new MySqlCommand("""
            INSERT INTO recurrence_rules (task_id, kind, interval_value, day_of_week, day_of_month)
            VALUES (@task_id, @kind, @interval_value, @day_of_week, @day_of_month);
            """, connection);
        insert.Parameters.AddWithValue("@task_id", task.Id);
        insert.Parameters.AddWithValue("@kind", (int)task.Recurrence.Kind);
        insert.Parameters.AddWithValue("@interval_value", task.Recurrence.Interval);
        insert.Parameters.AddWithValue("@day_of_week", (object?)task.Recurrence.DayOfWeek is null ? DBNull.Value : (int)task.Recurrence.DayOfWeek.Value);
        insert.Parameters.AddWithValue("@day_of_month", (object?)task.Recurrence.DayOfMonth ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LoadTagsAsync(
        MySqlConnection connection,
        IReadOnlyList<TaskItem> tasks,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        await using var command = new MySqlCommand("""
            SELECT tt.task_id, t.id, t.name, t.color
            FROM task_tags tt
            INNER JOIN tags t ON t.id = tt.tag_id;
            """, connection);
        var taskById = tasks.ToDictionary(t => t.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (taskById.TryGetValue(reader.GetInt64("task_id"), out var task))
            {
                task.Tags.Add(new Tag
                {
                    Id = reader.GetInt64("id"),
                    Name = reader.GetString("name"),
                    Color = reader.GetString("color")
                });
            }
        }
    }

    private static async Task SaveTagsAsync(MySqlConnection connection, TaskItem task, CancellationToken cancellationToken)
    {
        await using var delete = new MySqlCommand("DELETE FROM task_tags WHERE task_id = @task_id;", connection);
        delete.Parameters.AddWithValue("@task_id", task.Id);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var tag in task.Tags)
        {
            await using var saveTag = new MySqlCommand("""
                INSERT INTO tags (name, color)
                VALUES (@name, @color)
                ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);
                SELECT LAST_INSERT_ID();
                """, connection);
            saveTag.Parameters.AddWithValue("@name", tag.Name);
            saveTag.Parameters.AddWithValue("@color", tag.Color);
            tag.Id = Convert.ToInt64(await saveTag.ExecuteScalarAsync(cancellationToken));

            await using var link = new MySqlCommand("""
                INSERT IGNORE INTO task_tags (task_id, tag_id)
                VALUES (@task_id, @tag_id);
                """, connection);
            link.Parameters.AddWithValue("@task_id", task.Id);
            link.Parameters.AddWithValue("@tag_id", tag.Id);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool IsUndatedSingleTask(TaskItem task)
        => !TaskOccurrenceRules.IsRecurring(task) && task.DueAt is null;

    private static async Task EnsureReminderOffsetColumnAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var exists = new MySqlCommand("""
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'tasks'
              AND column_name = 'reminder_offset_minutes';
            """, connection);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            return;
        }

        await using var alter = new MySqlCommand("ALTER TABLE tasks ADD COLUMN reminder_offset_minutes INT NULL AFTER reminder_at;", connection);
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
