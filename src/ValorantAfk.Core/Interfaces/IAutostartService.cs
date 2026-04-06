namespace ValorantAfkBot.Core.Interfaces;

public interface IAutostartService
{
    bool IsEnabled();

    Task SetEnabledAsync(bool enabled, string executablePath, CancellationToken cancellationToken = default);
}
