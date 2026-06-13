using System.Runtime.InteropServices;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class KeyboardChordHotkeyService : IHotkeyService
{
    private const int VkOem3 = 0xC0;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Win32Native.LowLevelKeyboardProc _callback;
    private readonly HashSet<int> _requiredKeys = [];
    private IntPtr _hook;
    private bool _firedForCurrentChord;
    private bool _requiresControl;
    private bool _requiresShift;
    private bool _requiresAlt;
    private bool _requiresWin;

    public KeyboardChordHotkeyService()
    {
        _callback = HookCallback;
    }

    public event EventHandler? HotkeyPressed;

    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParseGesture(gesture))
        {
            return false;
        }

        _hook = Win32Native.SetWindowsHookEx(Win32Native.WhKeyboardLl, _callback, Win32Native.GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _pressedKeys.Clear();
        _firedForCurrentChord = false;
    }

    public void Dispose() => Unregister();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var vkCode = Marshal.ReadInt32(lParam);
            if (message is Win32Native.WmKeyDown or Win32Native.WmSysKeyDown)
            {
                _pressedKeys.Add(vkCode);
                if (!_firedForCurrentChord && IsChordPressed())
                {
                    _firedForCurrentChord = true;
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    return new IntPtr(1);
                }
            }
            else if (message is Win32Native.WmKeyUp or Win32Native.WmSysKeyUp)
            {
                _pressedKeys.Remove(vkCode);
                if (!IsChordPressed())
                {
                    _firedForCurrentChord = false;
                }
            }
        }

        return Win32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool TryParseGesture(string gesture)
    {
        _requiredKeys.Clear();
        _requiresControl = false;
        _requiresShift = false;
        _requiresAlt = false;
        _requiresWin = false;

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    _requiresControl = true;
                    break;
                case "SHIFT":
                    _requiresShift = true;
                    break;
                case "ALT":
                    _requiresAlt = true;
                    break;
                case "WIN":
                case "WINDOWS":
                    _requiresWin = true;
                    break;
                default:
                    var key = ParseKey(part);
                    if (key == 0)
                    {
                        return false;
                    }
                    _requiredKeys.Add(key);
                    break;
            }
        }

        return _requiredKeys.Count > 0;
    }

    private static int ParseKey(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (normalized is "~" or "`" or "OEM3" or "·")
        {
            return VkOem3;
        }

        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            return char.ToUpperInvariant(normalized[0]);
        }

        if (normalized.StartsWith('F') &&
            int.TryParse(normalized[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return 0x70 + functionKey - 1;
        }

        return 0;
    }

    private bool IsChordPressed()
    {
        return (!_requiresControl || IsAnyPressed(VkControl, VkLControl, VkRControl)) &&
               (!_requiresShift || IsAnyPressed(VkShift, VkLShift, VkRShift)) &&
               (!_requiresAlt || IsAnyPressed(VkMenu, VkLMenu, VkRMenu)) &&
               (!_requiresWin || IsAnyPressed(VkLWin, VkRWin)) &&
               _requiredKeys.All(key => _pressedKeys.Contains(key));
    }

    private bool IsAnyPressed(params int[] keys)
        => keys.Any(_pressedKeys.Contains);

    public static bool CanParse(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        return parts.Any(part => ParseKey(part) != 0);
    }

    public static bool UsesOem3Key(string gesture)
    {
        return gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(part => ParseKey(part) == VkOem3);
    }
}
