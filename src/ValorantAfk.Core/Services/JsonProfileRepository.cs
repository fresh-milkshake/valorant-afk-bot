using System.Text.Json;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public sealed class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _configurationFilePath;

    public JsonProfileRepository(string configurationFilePath)
    {
        _configurationFilePath = configurationFilePath;
    }

    public async Task<ConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configurationFilePath))
        {
            return ConfigurationNormalizer.Normalize(ConfigurationDocument.CreateDefault());
        }

        await using FileStream stream = File.OpenRead(_configurationFilePath);
        ConfigurationDocument? document = await JsonSerializer.DeserializeAsync<ConfigurationDocument>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return ConfigurationNormalizer.Normalize(document);
    }

    public async Task SaveAsync(ConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_configurationFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_configurationFilePath}.tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                ConfigurationNormalizer.Normalize(document),
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _configurationFilePath, true);
    }
}
