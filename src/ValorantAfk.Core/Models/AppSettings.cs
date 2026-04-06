namespace ValorantAfkBot.Core.Models;

public sealed record class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool MinimizeToTrayOnClose { get; init; } = true;

    public bool LaunchOnStartup { get; init; }

    public bool PersistLogsToDisk { get; init; } = true;

    public int MaxLogEntries { get; init; } = 500;

    public string? LastProfileId { get; init; }

    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = HotkeyBinding.CreateDefaults();
}
