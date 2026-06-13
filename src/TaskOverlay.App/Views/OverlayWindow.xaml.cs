using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskOverlay.App.Services;
using TaskOverlay.App.ViewModels;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;
using TaskOverlay.Infrastructure.SystemIntegration;

namespace TaskOverlay.App.Views;

public partial class OverlayWindow : Window, IOverlayWindowController, INotifyPropertyChanged
{
    private TaskListViewModel _viewModel;
    private readonly LocalSettingsStore _settingsStore;
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _openManagementWindow;
    private readonly Win32TopmostService _topmostService = new();
    private readonly IClickThroughService _clickThroughService = new Win32ClickThroughService();
    private readonly DispatcherTimer _topmostRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _boundsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private IHotkeyService? _hotkeyService;
    private string? _registeredHotkeyGesture;
    private bool _isExiting;
    private bool _isApplyingSettings;
    private bool _isEditMode;
    private double _viewOpacity = 0.78;
    private double? _compactHeightBeforeEdit;

    public OverlayWindow(
        TaskApplicationService tasks,
        ITaskRepository repository,
        LocalSettingsStore settingsStore,
        NotifyIcon notifyIcon,
        Action openManagementWindow)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _notifyIcon = notifyIcon;
        _openManagementWindow = openManagementWindow;
        _viewModel = new TaskListViewModel(tasks);
        DataContext = _viewModel;
        ApplySettings(_settingsStore.Current);
        Loaded += OnLoaded;
        Closing += OnClosing;
        LocationChanged += (_, _) => ScheduleWindowBoundsSave();
        SizeChanged += (_, _) => ScheduleWindowBoundsSave();
        IsVisibleChanged += (_, _) => ReassertTopmost();
        Deactivated += (_, _) => ReassertTopmost();
        _topmostRefreshTimer.Tick += (_, _) => ReassertTopmost();
        _topmostRefreshTimer.Start();
        _boundsSaveTimer.Tick += (_, _) =>
        {
            _boundsSaveTimer.Stop();
            SaveWindowBounds();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsTopmost { get; private set; }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (_isEditMode == value)
            {
                return;
            }

            _isEditMode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditMode)));
        }
    }

    public async Task ReloadAsync() => await _viewModel.LoadAsync();

    public void UpdateServices(TaskApplicationService tasks, ITaskRepository repository)
    {
        _viewModel.Dispose();
        _viewModel = new TaskListViewModel(tasks);
        DataContext = _viewModel;
        _ = _viewModel.LoadAsync();
    }

    public void PrepareForExit() => _isExiting = true;

    public void DisposeHotkey()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = null;
        _registeredHotkeyGesture = null;
    }

    public void ToggleTopmostFromTray()
    {
        TopmostToggle.IsChecked = !(TopmostToggle.IsChecked ?? false);
    }

    public void SetTopmost(bool enabled)
    {
        if (IsTopmost != enabled)
        {
            IsTopmost = enabled;
        }

        if (Topmost != enabled)
        {
            Topmost = enabled;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _topmostService.SetTopmost(hwnd, enabled);
        }
    }

    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _clickThroughService.SetClickThrough(hwnd, enabled);
        }
    }

    public void SetOpacity(double opacity)
    {
        _viewOpacity = Math.Clamp(opacity, 0.25, 1.0);
        ApplyVisualState(IsEditMode);
    }

    public void ShowOverlay()
    {
        Show();
        ReassertTopmost();
    }

    public void HideOverlay() => Hide();

    public void ToggleEditMode()
    {
        var enteringEditMode = !IsEditMode;
        if (enteringEditMode)
        {
            _compactHeightBeforeEdit = Height;
            if (Height < 260)
            {
                Height = 260;
            }
        }

        IsEditMode = enteringEditMode;
        EditPanel.Visibility = IsEditMode ? Visibility.Visible : Visibility.Collapsed;
        ViewHeader.Visibility = IsEditMode ? Visibility.Collapsed : Visibility.Visible;
        EditHeader.Visibility = IsEditMode ? Visibility.Visible : Visibility.Collapsed;
        ApplyVisualState(IsEditMode);
        SetClickThrough(!IsEditMode);
        if (IsEditMode)
        {
            ShowOverlay();
            Activate();
            QuickAddTextBox.Focus();
        }
        else if (_compactHeightBeforeEdit is { } compactHeight)
        {
            Height = Math.Max(MinHeight, compactHeight);
            _compactHeightBeforeEdit = null;
        }

        ReassertTopmost();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RegisterHotkey(_settingsStore.Current.Hotkey);
        ApplyVisualState(IsEditMode);
        SetClickThrough(!IsEditMode);
        SetTopmost(_settingsStore.Current.IsTopmost);
        await _viewModel.LoadAsync();
    }

    private static IHotkeyService CreateHotkeyService(IntPtr hwnd, string gesture)
    {
        return gesture.Equals("~+1", StringComparison.OrdinalIgnoreCase) ||
               gesture.Equals("`+1", StringComparison.OrdinalIgnoreCase) ||
               KeyboardChordHotkeyService.UsesOem3Key(gesture)
            ? new KeyboardChordHotkeyService()
            : new Win32HotkeyService(hwnd);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowBounds();
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _viewModel.Dispose();
        _topmostRefreshTimer.Stop();
        _boundsSaveTimer.Stop();
        DisposeHotkey();
    }

    public void ApplySettings(AppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            Left = settings.OverlayLeft;
            Top = settings.OverlayTop;
            Width = settings.OverlayWidth;
            Height = settings.OverlayHeight;
            TopmostToggle.IsChecked = settings.IsTopmost;
            SetTopmost(settings.IsTopmost);
            SetOpacity(settings.OverlayOpacity);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        if (IsLoaded && !string.Equals(_registeredHotkeyGesture, settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            RegisterHotkey(settings.Hotkey);
        }
    }

    private void ApplyVisualState(bool editMode)
    {
        Opacity = 1.0;
        Shell.Background = BuildShellBrush(editMode);
        Shell.BorderBrush = (System.Windows.Media.Brush)FindResource(editMode ? "EditBorderBrush" : "ViewBorderBrush");
        Shell.Padding = editMode ? new Thickness(10) : new Thickness(9);
        ShellShadow.Opacity = editMode ? 0.22 : 0.12;
        ShellShadow.BlurRadius = editMode ? 22 : 18;
    }

    private SolidColorBrush BuildShellBrush(bool editMode)
    {
        var opacity = _viewOpacity;
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0.25, 1.0) * 255);
        var color = editMode
            ? System.Windows.Media.Color.FromArgb(alpha, 10, 18, 32)
            : System.Windows.Media.Color.FromArgb(alpha, 17, 24, 39);
        return new SolidColorBrush(color);
    }

    private void ReassertTopmost()
    {
        if (IsVisible && IsTopmost)
        {
            SetTopmost(true);
        }
    }

    private void RegisterHotkey(string gesture)
    {
        var previousGesture = _registeredHotkeyGesture;
        DisposeHotkey();
        if (TryActivateHotkey(gesture))
        {
            return;
        }

        var restoredPrevious = !string.IsNullOrWhiteSpace(previousGesture) &&
                               !string.Equals(previousGesture, gesture, StringComparison.OrdinalIgnoreCase) &&
                               TryActivateHotkey(previousGesture);
        var message = restoredPrevious
            ? $"无法注册快捷键“{gesture}”，已保留原快捷键“{previousGesture}”。"
            : $"无法注册快捷键“{gesture}”，请在设置中修改。";
        _notifyIcon.ShowBalloonTip(5000, "快捷键注册失败", message, ToolTipIcon.Warning);
    }

    private bool TryActivateHotkey(string gesture)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var service = CreateHotkeyService(hwnd, gesture);
        service.HotkeyPressed += HotkeyService_OnHotkeyPressed;
        if (!service.Register(gesture))
        {
            service.Dispose();
            if (service is KeyboardChordHotkeyService || !KeyboardChordHotkeyService.CanParse(gesture))
            {
                return false;
            }

            var fallbackService = new KeyboardChordHotkeyService();
            fallbackService.HotkeyPressed += HotkeyService_OnHotkeyPressed;
            if (!fallbackService.Register(gesture))
            {
                fallbackService.Dispose();
                return false;
            }

            _hotkeyService = fallbackService;
            _registeredHotkeyGesture = gesture;
            return true;
        }

        _hotkeyService = service;
        _registeredHotkeyGesture = gesture;
        return true;
    }

    private void HotkeyService_OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(ToggleEditMode);
    }

    private void ScheduleWindowBoundsSave()
    {
        if (_isApplyingSettings || !IsLoaded)
        {
            return;
        }

        _boundsSaveTimer.Stop();
        _boundsSaveTimer.Start();
    }

    private void SaveWindowBounds()
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var settings = _settingsStore.Current;
        settings.OverlayLeft = Left;
        settings.OverlayTop = Top;
        settings.OverlayWidth = Width;
        settings.OverlayHeight = Height;
        settings.IsTopmost = IsTopmost;
        _settingsStore.Save(settings);
    }

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEditMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void EditButton_OnClick(object sender, RoutedEventArgs e) => ToggleEditMode();

    private void OpenManagement_OnClick(object sender, RoutedEventArgs e) => _openManagementWindow();

    private async void AddTask_OnClick(object sender, RoutedEventArgs e) => await _viewModel.AddAsync();

    private async void AddDailyTask_OnClick(object sender, RoutedEventArgs e) => await _viewModel.AddAsync(isDaily: true);

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void TaskCheck_OnClick(object sender, RoutedEventArgs e)
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

    private void TopmostToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        SetTopmost(true);
        SaveWindowBounds();
    }

    private void TopmostToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        SetTopmost(false);
        SaveWindowBounds();
    }
}
