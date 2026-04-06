using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Windows.Interop;

namespace ValorantAfkBot.Windows.Input;

public sealed class WindowMessageInputStrategy : IInputStrategy
{
    public InputStrategyType StrategyType => InputStrategyType.WindowMessage;

    public async Task SendKeyPressAsync(WindowDescriptor window, VirtualKey key, TimeSpan duration, CancellationToken cancellationToken)
    {
        SendKeyDown(window.Handle, key);
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        SendKeyUp(window.Handle, key);
    }

    public async Task SendChordAsync(
        WindowDescriptor window,
        IReadOnlyCollection<VirtualKey> keys,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        foreach (VirtualKey key in keys)
        {
            SendKeyDown(window.Handle, key);
        }

        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);

        foreach (VirtualKey key in keys.Reverse())
        {
            SendKeyUp(window.Handle, key);
        }
    }

    private static void SendKeyDown(nint handle, VirtualKey key) =>
        _ = NativeMethods.PostMessageW(handle, NativeMethods.WmKeyDown, (nint)(ushort)key, nint.Zero);

    private static void SendKeyUp(nint handle, VirtualKey key) =>
        _ = NativeMethods.PostMessageW(handle, NativeMethods.WmKeyUp, (nint)(ushort)key, nint.Zero);
}
