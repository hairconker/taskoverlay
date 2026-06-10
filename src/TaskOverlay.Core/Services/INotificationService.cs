namespace TaskOverlay.Core.Services;

public interface INotificationService
{
    Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
