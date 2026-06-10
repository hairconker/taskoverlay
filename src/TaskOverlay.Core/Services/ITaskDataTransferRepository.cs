namespace TaskOverlay.Core.Services;

public interface ITaskDataTransferRepository
{
    string DataFilePath { get; }
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}
