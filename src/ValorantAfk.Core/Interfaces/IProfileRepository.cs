using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Interfaces;

public interface IProfileRepository
{
    Task<ConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ConfigurationDocument document, CancellationToken cancellationToken = default);
}
