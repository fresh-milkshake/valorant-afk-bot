using System.Diagnostics;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Windows.Interop;

namespace ValorantAfkBot.Windows.Windowing;

public sealed class Win32WindowLocator : IWindowLocator
{
    private readonly IReadOnlySet<string> _processNames;
    private readonly int _currentProcessId = Environment.ProcessId;

    public Win32WindowLocator(
        string titleContains = "VALORANT",
        IEnumerable<string>? processNames = null)
    {
        _processNames = new HashSet<string>(processNames ?? ["VALORANT-Win64-Shipping"], StringComparer.OrdinalIgnoreCase);
    }

    public Task<WindowDescriptor?> FindWindowAsync(CancellationToken cancellationToken)
    {
        WindowDescriptor? result = null;

        _ = NativeMethods.EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            string title = NativeMethods.GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (processId == _currentProcessId)
            {
                return true;
            }

            string? processName = TryGetProcessName((int)processId);
            bool processMatch = processName is not null && _processNames.Contains(processName);
            if (!processMatch)
            {
                return true;
            }

            result = new WindowDescriptor(handle, (int)processId, title, processName);
            return false;
        }, nint.Zero);

        return Task.FromResult(result);
    }

    public Task<bool> IsWindowAvailableAsync(WindowDescriptor descriptor, CancellationToken cancellationToken) =>
        Task.FromResult(!cancellationToken.IsCancellationRequested
            && NativeMethods.IsWindow(descriptor.Handle)
            && NativeMethods.IsWindowVisible(descriptor.Handle));

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            return Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
