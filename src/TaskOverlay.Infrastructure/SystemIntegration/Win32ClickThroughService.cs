using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class Win32ClickThroughService : IClickThroughService
{
    public void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        var style = Win32Native.GetWindowLongPtr(hwnd, Win32Native.GwlExStyle).ToInt64();
        style = enabled
            ? style | Win32Native.WsExTransparent | Win32Native.WsExToolWindow
            : style & ~Win32Native.WsExTransparent;

        Win32Native.SetWindowLongPtr(hwnd, Win32Native.GwlExStyle, new IntPtr(style));
    }
}
