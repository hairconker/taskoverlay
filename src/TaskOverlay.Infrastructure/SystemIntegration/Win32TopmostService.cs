namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class Win32TopmostService
{
    public void SetTopmost(IntPtr hwnd, bool enabled)
    {
        Win32Native.SetWindowPos(
            hwnd,
            enabled ? Win32Native.HwndTopmost : Win32Native.HwndNotTopmost,
            0,
            0,
            0,
            0,
            Win32Native.SwpNoMove | Win32Native.SwpNoSize | Win32Native.SwpNoActivate);
    }
}
