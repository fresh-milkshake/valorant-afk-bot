using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Interfaces;

public interface IAntiAfkEngine
{
    EngineStatus Status { get; }

    ProfileSettings? ActiveProfile { get; }

    event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;

    Task StartAsync(ProfileSettings profile, CancellationToken cancellationToken = default);

    Task StopAsync();

    Task PauseAsync();

    Task ResumeAsync();

    void UpdateProfile(ProfileSettings profile);
}
