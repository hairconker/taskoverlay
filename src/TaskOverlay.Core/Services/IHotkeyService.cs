namespace TaskOverlay.Core.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    bool Register(string gesture);
    void Unregister();
}
