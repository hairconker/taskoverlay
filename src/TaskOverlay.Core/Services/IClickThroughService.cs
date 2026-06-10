namespace TaskOverlay.Core.Services;

public interface IClickThroughService
{
    void SetClickThrough(IntPtr hwnd, bool enabled);
}
