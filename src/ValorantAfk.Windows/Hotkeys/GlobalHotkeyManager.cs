using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Windows.Interop;

namespace ValorantAfkBot.Windows.Hotkeys;

public sealed class GlobalHotkeyManager : IDisposable
{
    public const int HotkeyMessageId = NativeMethods.WmHotKey;

    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private nint _windowHandle;

    public void Attach(nint windowHandle) => _windowHandle = windowHandle;

    public IReadOnlyList<HotkeyBinding> Register(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();

        List<HotkeyBinding> failed = [];
        foreach ((HotkeyBinding binding, int index) in bindings.Select((binding, index) => (binding, index)))
        {
            uint modifiers = ToNativeModifiers(binding.Modifiers);
            bool success = NativeMethods.RegisterHotKey(_windowHandle, index + 1, modifiers, (uint)binding.VirtualKey);
            if (success)
            {
                _registered[index + 1] = binding;
            }
            else
            {
                failed.Add(binding);
            }
        }

        return failed;
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered.Keys.ToArray())
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, id);
            _registered.Remove(id);
        }
    }

    public void Dispose()
        => UnregisterAll();

    public bool TryGetAction(int hotkeyId, out HotkeyAction action)
    {
        if (_registered.TryGetValue(hotkeyId, out HotkeyBinding? binding))
        {
            action = binding.Action;
            return true;
        }

        action = default;
        return false;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        const uint modAlt = 0x0001;
        const uint modControl = 0x0002;
        const uint modShift = 0x0004;
        const uint modWin = 0x0008;

        uint value = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            value |= modAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            value |= modControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            value |= modShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            value |= modWin;
        }

        return value;
    }
}
