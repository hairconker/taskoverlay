using System.Windows;
using System.Windows.Threading;
using TaskOverlay.App.Services;

namespace TaskOverlay.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private AppBootstrapper? _bootstrapper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown(0);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _bootstrapper = new AppBootstrapper();
        _bootstrapper.Start(e.Args.Any(arg => arg.Equals("--manage", StringComparison.OrdinalIgnoreCase)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bootstrapper?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"程序刚才遇到一个未处理错误，但已拦截以避免直接退出：\n{e.Exception.Message}",
            "TaskOverlay 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }
}
