using System.Windows.Interop;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class Win32HotkeyService(IntPtr hwnd) : IHotkeyService
{
    private const int HotkeyId = 0x544F;
    private const uint VkOem3 = 0xC0;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParseGesture(gesture, out var modifiers, out var key))
        {
            return false;
        }

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        _registered = Win32Native.RegisterHotKey(hwnd, HotkeyId, modifiers, key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Win32Native.UnregisterHotKey(hwnd, HotkeyId);
            _registered = false;
        }

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    public void Dispose() => Unregister();

    private IntPtr WndProc(IntPtr sourceHwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Win32Native.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParseGesture(string gesture, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= Win32Native.ModControl;
                    break;
                case "ALT":
                    modifiers |= Win32Native.ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= Win32Native.ModShift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= Win32Native.ModWin;
                    break;
                default:
                    if (IsOem3Key(part))
                    {
                        key = VkOem3;
                    }
                    else if (part.Length == 1)
                    {
                        key = char.ToUpperInvariant(part[0]);
                    }
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out var fKey) && fKey is >= 1 and <= 24)
                    {
                        key = (uint)(0x70 + fKey - 1);
                    }
                    break;
            }
        }

        return modifiers != 0 && key != 0;
    }

    private static bool IsOem3Key(string value)
    {
        return value.Equals("`", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("~", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("OEM3", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("·", StringComparison.OrdinalIgnoreCase);
    }
}
