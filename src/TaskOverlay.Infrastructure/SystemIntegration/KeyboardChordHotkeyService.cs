using System.Runtime.InteropServices;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.SystemIntegration;

public sealed class KeyboardChordHotkeyService : IHotkeyService
{
    private const int VkOem3 = 0xC0;
    private const int Vk1 = 0x31;
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Win32Native.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _firedForCurrentChord;
    private int _firstKey;
    private int _secondKey;

    public KeyboardChordHotkeyService()
    {
        _callback = HookCallback;
    }

    public event EventHandler? HotkeyPressed;

    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParseGesture(gesture, out _firstKey, out _secondKey))
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
                if (!_firedForCurrentChord && _pressedKeys.Contains(_firstKey) && _pressedKeys.Contains(_secondKey))
                {
                    _firedForCurrentChord = true;
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    return new IntPtr(1);
                }
            }
            else if (message is Win32Native.WmKeyUp or Win32Native.WmSysKeyUp)
            {
                _pressedKeys.Remove(vkCode);
                if (!_pressedKeys.Contains(_firstKey) || !_pressedKeys.Contains(_secondKey))
                {
                    _firedForCurrentChord = false;
                }
            }
        }

        return Win32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool TryParseGesture(string gesture, out int firstKey, out int secondKey)
    {
        firstKey = 0;
        secondKey = 0;

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        firstKey = ParseKey(parts[0]);
        secondKey = ParseKey(parts[1]);
        return firstKey != 0 && secondKey != 0;
    }

    private static int ParseKey(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "~" or "`" or "OEM3" or "·" => VkOem3,
            "1" => Vk1,
            _ => 0
        };
    }
}
