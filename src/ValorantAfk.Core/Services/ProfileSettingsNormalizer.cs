using System.Text.RegularExpressions;
using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public static partial class ProfileSettingsNormalizer
{
    private const double RouteCanvasMin = -0.5;
    private const double RouteCanvasMax = 1.5;

    [GeneratedRegex("[^WASD]", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InvalidPathCharacters();

    public static ProfileSettings Normalize(ProfileSettings profile)
    {
        string sanitizedPath = InvalidPathCharacters().Replace(profile.MovementPath ?? string.Empty, string.Empty).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(sanitizedPath))
        {
            sanitizedPath = "WASD";
        }

        List<RouteCanvasPoint> normalizedRoute = [];
        foreach (RouteCanvasPoint point in profile.RouteCanvasPoints ?? [])
        {
            RouteCanvasPoint normalizedPoint = new()
            {
                X = Clamp(point.X, RouteCanvasMin, RouteCanvasMax),
                Y = Clamp(point.Y, RouteCanvasMin, RouteCanvasMax),
            };

            if (normalizedRoute.Count == 0 || GetDistance(normalizedRoute[^1], normalizedPoint) >= 0.01)
            {
                normalizedRoute.Add(normalizedPoint);
            }
        }

        if (normalizedRoute.Count < 2)
        {
            normalizedRoute = ProfileSettings.CreateDefaultRouteCanvas().ToList();
        }

        double minPause = Clamp(profile.MinPauseSeconds, 0.1, 10.0);
        double maxPause = Clamp(profile.MaxPauseSeconds, minPause, 15.0);
        AntiAfkMode normalizedMode = Enum.IsDefined(typeof(AntiAfkMode), profile.Mode) ? profile.Mode : AntiAfkMode.Jumping;
        MovementPattern normalizedPattern = Enum.IsDefined(typeof(MovementPattern), profile.PatternType)
            ? profile.PatternType
            : MovementPattern.Random;

        if (normalizedMode == AntiAfkMode.Wasd && normalizedPattern == MovementPattern.RouteCanvas)
        {
            normalizedMode = AntiAfkMode.PathFollow;
        }

        if (normalizedMode == AntiAfkMode.PathFollow)
        {
            normalizedPattern = MovementPattern.RouteCanvas;
        }

        return profile with
        {
            Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id,
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Profile" : profile.Name.Trim(),
            Mode = normalizedMode,
            InputStrategy = Enum.IsDefined(typeof(InputStrategyType), profile.InputStrategy)
                ? profile.InputStrategy
                : InputStrategyType.WindowMessage,
            PatternType = normalizedPattern,
            JumpDelaySeconds = Clamp(profile.JumpDelaySeconds, 0.1, 60.0),
            KeyPressDelaySeconds = Clamp(profile.KeyPressDelaySeconds, 0.1, 5.0),
            MovementPath = sanitizedPath,
            RouteCanvasPoints = normalizedRoute,
            MovementIntensity = Clamp(profile.MovementIntensity, 0.1, 1.0),
            DirectionChangeFrequency = Clamp(profile.DirectionChangeFrequency, 0.1, 1.0),
            ActionProbability = Clamp(profile.ActionProbability, 0.0, 1.0),
            StrafePreference = Clamp(profile.StrafePreference, 0.0, 1.0),
            MovementSmoothness = Clamp(profile.MovementSmoothness, 0.1, 1.0),
            PauseFrequency = Clamp(profile.PauseFrequency, 0.0, 1.0),
            MinPauseSeconds = minPause,
            MaxPauseSeconds = maxPause,
        };
    }

    private static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    private static double GetDistance(RouteCanvasPoint first, RouteCanvasPoint second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
