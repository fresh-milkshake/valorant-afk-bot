using System.Runtime.InteropServices;
using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Windows.Interop;

namespace ValorantAfkBot.Windows.Input;

public sealed class ForegroundSendInputStrategy : IInputStrategy
{
    public InputStrategyType StrategyType => InputStrategyType.ForegroundSendInput;

    public async Task SendKeyPressAsync(WindowDescriptor window, VirtualKey key, TimeSpan duration, CancellationToken cancellationToken)
    {
        FocusWindow(window.Handle);
        SendInputs(CreateKeyDown(key));
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        SendInputs(CreateKeyUp(key));
    }

    public async Task SendChordAsync(
        WindowDescriptor window,
        IReadOnlyCollection<VirtualKey> keys,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        FocusWindow(window.Handle);
        SendInputs(keys.Select(CreateKeyDown).ToArray());
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        SendInputs(keys.Reverse().Select(CreateKeyUp).ToArray());
    }

    private static void FocusWindow(nint handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            _ = NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        }

        _ = NativeMethods.SetForegroundWindow(handle);
    }

    private static NativeMethods.INPUT CreateKeyDown(VirtualKey key) => new()
    {
        type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)key,
            },
        },
    };

    private static NativeMethods.INPUT CreateKeyUp(VirtualKey key) => new()
    {
        type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)key,
                dwFlags = NativeMethods.KeyEventFKeyUp,
            },
        },
    };

    private static void SendInputs(params NativeMethods.INPUT[] inputs)
    {
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
