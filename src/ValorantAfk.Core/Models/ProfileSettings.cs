using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Models;

public sealed record class ProfileSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "Default";

    public AntiAfkMode Mode { get; init; } = AntiAfkMode.Jumping;

    public InputStrategyType InputStrategy { get; init; } = InputStrategyType.WindowMessage;

    public double JumpDelaySeconds { get; init; } = 5.0;

    public double KeyPressDelaySeconds { get; init; } = 0.5;

    public string MovementPath { get; init; } = "WASD";

    public MovementPattern PatternType { get; init; } = MovementPattern.Random;

    public IReadOnlyList<RouteCanvasPoint> RouteCanvasPoints { get; init; } = CreateDefaultRouteCanvas();

    public double MovementIntensity { get; init; } = 0.7;

    public double DirectionChangeFrequency { get; init; } = 0.3;

    public double ActionProbability { get; init; } = 0.4;

    public double StrafePreference { get; init; } = 0.5;

    public double MovementSmoothness { get; init; } = 0.6;

    public double PauseFrequency { get; init; } = 0.2;

    public double MinPauseSeconds { get; init; } = 0.5;

    public double MaxPauseSeconds { get; init; } = 2.0;

    public static ProfileSettings CreateDefault() => new();

    public static IReadOnlyList<RouteCanvasPoint> CreateDefaultRouteCanvas() =>
    [
        new RouteCanvasPoint { X = 0.22, Y = 0.22 },
        new RouteCanvasPoint { X = 0.78, Y = 0.22 },
        new RouteCanvasPoint { X = 0.78, Y = 0.78 },
        new RouteCanvasPoint { X = 0.22, Y = 0.78 },
    ];
}
