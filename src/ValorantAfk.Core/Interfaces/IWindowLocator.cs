using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Interfaces;

public interface IWindowLocator
{
    Task<WindowDescriptor?> FindWindowAsync(CancellationToken cancellationToken);

    Task<bool> IsWindowAvailableAsync(WindowDescriptor descriptor, CancellationToken cancellationToken);
}
