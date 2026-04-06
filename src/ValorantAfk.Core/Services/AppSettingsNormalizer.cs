using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public static class AppSettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        IReadOnlyList<HotkeyBinding> hotkeys = settings.Hotkeys?.Where(static binding => binding is not null).ToList()
            ?? [];

        if (hotkeys.Count == 0)
        {
            hotkeys = HotkeyBinding.CreateDefaults();
        }

        Dictionary<Enums.HotkeyAction, HotkeyBinding> unique = hotkeys
            .GroupBy(static binding => binding.Action)
            .ToDictionary(static group => group.Key, static group => group.First());

        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            MaxLogEntries = Math.Clamp(settings.MaxLogEntries, 100, 5000),
            Hotkeys = unique.Values.OrderBy(static binding => binding.Action).ToList(),
        };
    }
}
