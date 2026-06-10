using System.Threading;

namespace TaskOverlay.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\TaskOverlay.Desktop.SingleInstance";
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
