namespace ValorantAfkBot.Core.Models;

public sealed record class ConfigurationDocument
{
    public int SchemaVersion { get; init; } = AppSettings.CurrentSchemaVersion;

    public AppSettings AppSettings { get; init; } = new();

    public IReadOnlyList<ProfileSettings> Profiles { get; init; } = [ProfileSettings.CreateDefault()];

    public static ConfigurationDocument CreateDefault() => new();
}
