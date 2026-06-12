using System.Windows.Forms;
using System.Windows.Threading;
using System.IO;
using TaskOverlay.App.Views;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;
using TaskOverlay.Infrastructure.MySql;
using TaskOverlay.Infrastructure.Storage;

namespace TaskOverlay.App.Services;

public sealed class AppBootstrapper : IDisposable
{
    private readonly LocalSettingsStore _settingsStore = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ExternalTaskProposalStore _proposals;
    private readonly GoalApplicationService _goals;
    private readonly LocalTaskApiService _api;
    private ITaskRepository _repository;
    private TaskApplicationService _tasks;
    private readonly DispatcherTimer _reminderTimer = new();
    private OverlayWindow? _overlayWindow;
    private ManagementWindow? _managementWindow;
    private bool _isDisposed;
    private bool _isReminderPolling;

    public AppBootstrapper()
    {
        _settingsStore.Load();

        _repository = new JsonTaskRepository(() => _settingsStore.Current);
        _tasks = new TaskApplicationService(_repository);
        _proposals = new ExternalTaskProposalStore(Path.GetDirectoryName(_settingsStore.SettingsFilePath)!);
        _goals = new GoalApplicationService(new JsonGoalRepository(Path.GetDirectoryName(_settingsStore.SettingsFilePath)!));
        _api = new LocalTaskApiService(() => _tasks, _proposals, () => _goals, () => _settingsStore.Current);
        _notifyIcon = BuildNotifyIcon();
    }

    public void Start(bool openManagementWindow = false)
    {
        _overlayWindow = new OverlayWindow(_tasks, _repository, _settingsStore, _notifyIcon, OpenManagementWindow);
        _overlayWindow.Show();
        _notifyIcon.Visible = true;

        _ = RunStartupTaskAsync();
        StartReminderLoop();
        if (openManagementWindow)
        {
            OpenManagementWindow();
        }
    }

    public void OpenManagementWindow()
    {
        if (_managementWindow is null)
        {
            _managementWindow = new ManagementWindow(
                _tasks,
                _repository,
                _settingsStore,
                _proposals,
                _goals,
                ApplySettingsAsync,
                opacity => _overlayWindow?.SetOpacity(opacity),
                ExitApplication);
            _managementWindow.Closed += (_, _) => _managementWindow = null;
        }

        _managementWindow.Show();
        _managementWindow.Activate();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _reminderTimer.Stop();
        _overlayWindow?.DisposeHotkey();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _api.Dispose();
    }

    private NotifyIcon BuildNotifyIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏悬浮窗", null, (_, _) => ToggleOverlay());
        menu.Items.Add("打开管理面板", null, (_, _) => OpenManagementWindow());
        menu.Items.Add("切换置顶", null, (_, _) => _overlayWindow?.ToggleTopmostFromTray());
        menu.Items.Add("退出程序", null, (_, _) => ExitApplication());

        return new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "悬浮任务列表",
            ContextMenuStrip = menu
        };
    }

    private async Task InitializeStorageAsync()
    {
        try
        {
            var error = await SwitchRepositoryAsync();
            if (error is not null)
            {
                _notifyIcon.ShowBalloonTip(6000, "已使用本地存储", $"MySQL 不可用，已切换到本地 JSON：{error}", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(6000, "启动初始化失败", $"已保留当前界面，请检查数据文件或设置：{ex.Message}", ToolTipIcon.Warning);
        }
    }

    private async Task<string?> ApplySettingsAsync()
    {
        _overlayWindow?.ApplySettings(_settingsStore.Current);
        try
        {
            var error = await SwitchRepositoryAsync();
            RestartApi();
            return error;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task<string?> SwitchRepositoryAsync()
    {
        string? error = null;
        if (_settingsStore.Current.StorageBackend == TaskStorageBackend.Json)
        {
            _repository = new JsonTaskRepository(() => _settingsStore.Current);
            await _repository.InitializeAsync().ConfigureAwait(false);
        }
        else
        {
            var mysql = new MySqlTaskRepository(() => _settingsStore.Current);
            try
            {
                await mysql.InitializeAsync().ConfigureAwait(false);
                _repository = mysql;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _settingsStore.Current.StorageBackend = TaskStorageBackend.Json;
                _settingsStore.Save(_settingsStore.Current);
                _repository = new JsonTaskRepository(() => _settingsStore.Current);
                await _repository.InitializeAsync().ConfigureAwait(false);
            }
        }

        _tasks = new TaskApplicationService(_repository);
        RebindWindows();
        await ReloadOverlayAsync();
        return error;
    }

    private void RebindWindows()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            _overlayWindow?.UpdateServices(_tasks, _repository);
            _managementWindow?.UpdateServices(_tasks, _repository);
            return;
        }

        dispatcher.Invoke(() =>
        {
            _overlayWindow?.UpdateServices(_tasks, _repository);
            _managementWindow?.UpdateServices(_tasks, _repository);
        });
    }

    private async Task ReloadOverlayAsync()
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            if (_overlayWindow is not null)
            {
                await _overlayWindow.ReloadAsync();
            }

            return;
        }

        await dispatcher.InvokeAsync(async () =>
        {
            if (_overlayWindow is not null)
            {
                await _overlayWindow.ReloadAsync();
            }
        }).Task.Unwrap();
    }

    private async Task RunStartupTaskAsync()
    {
        await InitializeStorageAsync();
        await InitializeGoalsAsync();
        RestartApi();
    }

    private async Task InitializeGoalsAsync()
    {
        try
        {
            await _goals.InitializeAsync();
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(6000, "目标库初始化失败", ex.Message, ToolTipIcon.Warning);
        }
    }

    private void RestartApi()
    {
        try
        {
            _api.Restart();
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(6000, "本地 API 启动失败", ex.Message, ToolTipIcon.Warning);
        }
    }

    private void StartReminderLoop()
    {
        _reminderTimer.Interval = TimeSpan.FromSeconds(30);
        _reminderTimer.Tick += async (_, _) =>
        {
            if (_isReminderPolling)
            {
                return;
            }

            _isReminderPolling = true;
            try
            {
                var due = await _repository.GetDueRemindersAsync(DateTime.Now);
                foreach (var task in due.Take(3))
                {
                    _notifyIcon.ShowBalloonTip(5000, "任务提醒", task.Title, ToolTipIcon.Info);
                    await _tasks.MarkReminderDeliveredAsync(task.Id);
                }
            }
            catch
            {
                // Background reminder polling must not interrupt foreground use.
            }
            finally
            {
                _isReminderPolling = false;
            }
        };
        _reminderTimer.Start();
    }

    private void ToggleOverlay()
    {
        if (_overlayWindow is null)
        {
            return;
        }

        if (_overlayWindow.IsVisible)
        {
            _overlayWindow.Hide();
        }
        else
        {
            _overlayWindow.ShowOverlay();
            _overlayWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        _overlayWindow?.PrepareForExit();
        _overlayWindow?.Close();
        _managementWindow?.Close();
        Dispose();
        System.Windows.Application.Current.Shutdown(0);
    }
}
