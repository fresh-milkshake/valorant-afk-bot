using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public static class ConfigurationNormalizer
{
    public static ConfigurationDocument Normalize(ConfigurationDocument? document)
    {
        document ??= ConfigurationDocument.CreateDefault();

        List<ProfileSettings> profiles = document.Profiles?
            .Select(ProfileSettingsNormalizer.Normalize)
            .GroupBy(static profile => profile.Id)
            .Select(static group => group.First())
            .ToList() ?? [];

        if (profiles.Count == 0)
        {
            profiles.Add(ProfileSettings.CreateDefault());
        }

        AppSettings settings = AppSettingsNormalizer.Normalize(document.AppSettings ?? new AppSettings());
        string selectedProfileId = profiles.Any(profile => profile.Id == settings.LastProfileId)
            ? settings.LastProfileId!
            : profiles[0].Id;

        return new ConfigurationDocument
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            AppSettings = settings with { LastProfileId = selectedProfileId },
            Profiles = profiles,
        };
    }
}
