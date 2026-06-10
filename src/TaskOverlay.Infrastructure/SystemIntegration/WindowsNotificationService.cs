using System.Windows.Forms;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class WindowsNotificationService(NotifyIcon notifyIcon) : INotificationService
{
    public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = message;
            notifyIcon.ShowBalloonTip(5000);
        }

        return Task.CompletedTask;
    }
}
