using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Interfaces;

public interface IInputStrategy
{
    InputStrategyType StrategyType { get; }

    Task SendKeyPressAsync(WindowDescriptor window, VirtualKey key, TimeSpan duration, CancellationToken cancellationToken);

    Task SendChordAsync(WindowDescriptor window, IReadOnlyCollection<VirtualKey> keys, TimeSpan duration, CancellationToken cancellationToken);
}
