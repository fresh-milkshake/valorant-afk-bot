using System.Threading;
using ValorantAfkBot.Windows.Interop;

namespace ValorantAfkBot.Windows.SingleInstance;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceCoordinator(string appId)
    {
        _mutex = new Mutex(true, $@"Local\{appId}", out bool createdNew);
        IsPrimaryInstance = createdNew;
        ActivateMessageId = NativeMethods.RegisterWindowMessageW($"{appId}.Activate");
    }

    public bool IsPrimaryInstance { get; }

    public uint ActivateMessageId { get; }

    public bool SignalExistingInstance()
    {
        if (IsPrimaryInstance)
        {
            return false;
        }

        return NativeMethods.PostMessageW((nint)0xffff, ActivateMessageId, 0, nint.Zero);
    }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
