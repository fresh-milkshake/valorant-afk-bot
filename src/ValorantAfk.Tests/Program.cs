using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Core.Services;

List<(string Name, Action Test)> tests =
[
    ("Hotkey formatter roundtrip", TestHotkeyRoundtrip),
    ("Profile normalization clamps values", TestProfileNormalization),
    ("Configuration repository roundtrip", TestRepositoryRoundtrip),
    ("Movement planner stays inside bounds", TestMovementPlannerBounds),
    ("Engine transitions through running and stopped", TestEngineTransitions),
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"[FAIL] {name}: {exception.Message}");
    }
}

return failures;

static void TestHotkeyRoundtrip()
{
    bool parsed = HotkeyBindingFormatter.TryParse("Ctrl+Alt+F10", HotkeyAction.Stop, out HotkeyBinding? binding);
    Assert(parsed && binding is not null, "Hotkey should parse");
    Assert(HotkeyBindingFormatter.ToDisplayString(binding!) == "Ctrl+Alt+F10", "Hotkey should format back");
}

static void TestProfileNormalization()
{
    ProfileSettings normalized = ProfileSettingsNormalizer.Normalize(new ProfileSettings
    {
        Name = "",
        JumpDelaySeconds = -5,
        KeyPressDelaySeconds = 99,
        MovementPath = "wzsd!",
        MovementIntensity = 10,
        DirectionChangeFrequency = 0,
        ActionProbability = 10,
        MinPauseSeconds = 5,
        MaxPauseSeconds = 1,
        RouteCanvasPoints =
        [
            new RouteCanvasPoint { X = -2, Y = 0.5 },
            new RouteCanvasPoint { X = 1.7, Y = 2.2 },
        ],
    });

    Assert(normalized.Name == "Profile", "Empty name should fallback");
    Assert(normalized.JumpDelaySeconds == 0.1, "Jump delay should clamp");
    Assert(normalized.KeyPressDelaySeconds == 5.0, "Key press should clamp");
    Assert(normalized.MovementPath == "WSD", "Movement path should strip invalid characters");
    Assert(normalized.MaxPauseSeconds >= normalized.MinPauseSeconds, "Pause range should normalize");
    Assert(normalized.RouteCanvasPoints.All(point => point.X is >= -0.5 and <= 1.5), "Route points X should clamp");
    Assert(normalized.RouteCanvasPoints.All(point => point.Y is >= -0.5 and <= 1.5), "Route points Y should clamp");

    ProfileSettings routeCanvasUpgrade = ProfileSettingsNormalizer.Normalize(new ProfileSettings
    {
        Mode = AntiAfkMode.Wasd,
        PatternType = MovementPattern.RouteCanvas,
    });

    Assert(routeCanvasUpgrade.Mode == AntiAfkMode.PathFollow, "Route canvas should upgrade to path follow mode");
    Assert(routeCanvasUpgrade.PatternType == MovementPattern.RouteCanvas, "Path follow should keep route canvas pattern");

    ProfileSettings forcedRouteCanvas = ProfileSettingsNormalizer.Normalize(new ProfileSettings
    {
        Mode = AntiAfkMode.PathFollow,
        PatternType = MovementPattern.Random,
    });

    Assert(forcedRouteCanvas.PatternType == MovementPattern.RouteCanvas, "Path follow mode should force route canvas pattern");
}

static void TestRepositoryRoundtrip()
{
    string filePath = Path.Combine(Path.GetTempPath(), $"valorant-afk-tests-{Guid.NewGuid():N}.json");
    try
    {
        JsonProfileRepository repository = new(filePath);
        ConfigurationDocument input = new()
        {
            AppSettings = new AppSettings
            {
                LaunchOnStartup = true,
                LastProfileId = "p1",
                Hotkeys =
                [
                    new HotkeyBinding
                    {
                        Action = HotkeyAction.Start,
                        Modifiers = HotkeyModifiers.Control,
                        VirtualKey = (int)VirtualKey.F9,
                    },
                ],
            },
            Profiles =
            [
                new ProfileSettings { Id = "p1", Name = "One" },
            ],
        };

        repository.SaveAsync(input).GetAwaiter().GetResult();
        ConfigurationDocument output = repository.LoadAsync().GetAwaiter().GetResult();

        Assert(output.AppSettings.LaunchOnStartup, "LaunchOnStartup should persist");
        Assert(output.Profiles.Count == 1, "Single profile should persist");
        Assert(output.AppSettings.LastProfileId == "p1", "Selected profile should persist");
    }
    finally
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

static void TestMovementPlannerBounds()
{
    ProfileSettings profile = ProfileSettings.CreateDefault();
    Random random = new(42);

    TimeSpan jump = MovementPlanner.RandomizedJumpDelay(profile, random);
    Assert(jump >= TimeSpan.FromSeconds(3), "Jump delay lower bound");
    Assert(jump <= TimeSpan.FromSeconds(7), "Jump delay upper bound");

    IReadOnlyList<string> pattern = MovementPlanner.BuildPattern(profile with { PatternType = MovementPattern.Circle }, random);
    Assert(pattern.Count == 8, "Circle pattern should be deterministic");

    IReadOnlyList<string> routePattern = MovementPlanner.BuildPattern(profile with
    {
        PatternType = MovementPattern.RouteCanvas,
        RouteCanvasPoints =
        [
            new RouteCanvasPoint { X = 0.1, Y = 0.1 },
            new RouteCanvasPoint { X = 0.9, Y = 0.1 },
            new RouteCanvasPoint { X = 0.9, Y = 0.9 },
            new RouteCanvasPoint { X = 0.1, Y = 0.9 },
        ],
    }, random);

    Assert(routePattern.Count >= 4, "Route canvas should expand into a movement sequence");
    Assert(routePattern.Contains("D"), "Route canvas should include right movement");
    Assert(routePattern.Contains("S"), "Route canvas should include down movement");
    Assert(routePattern.Any(direction => direction.Length == 2), "Route canvas should use diagonal transitions for smoother turns");
    Assert(routePattern.Count is >= 24 and <= 80, "Route canvas scale should stay readable instead of exploding into huge traversals");

    IReadOnlyList<string> linePattern = MovementPlanner.BuildPattern(profile with
    {
        PatternType = MovementPattern.RouteCanvas,
        RouteCanvasPoints =
        [
            new RouteCanvasPoint { X = 0.2, Y = 0.5 },
            new RouteCanvasPoint { X = 0.8, Y = 0.5 },
        ],
    }, random);

    Assert(linePattern.Contains("D"), "Open route should move forward along the line");
    Assert(linePattern.Contains("A"), "Open route should return along the line");
    Assert(linePattern.All(direction => direction is "D" or "A"), "Horizontal route should not introduce unrelated directions");

    IReadOnlyList<string> diagonalPattern = MovementPlanner.BuildPattern(profile with
    {
        PatternType = MovementPattern.RouteCanvas,
        RouteCanvasPoints =
        [
            new RouteCanvasPoint { X = 0.2, Y = 0.8 },
            new RouteCanvasPoint { X = 0.5, Y = 0.5 },
            new RouteCanvasPoint { X = 0.8, Y = 0.2 },
        ],
    }, random);

    Assert(diagonalPattern.Any(direction => direction is "WD" or "DW"), "Diagonal route should preserve diagonal movement");

    TimeSpan genericDuration = MovementPlanner.RandomizedKeyPress(profile, new Random(11));
    TimeSpan routeDuration = MovementPlanner.RandomizedRouteFollowKeyPress(profile, new Random(11));
    TimeSpan genericPause = MovementPlanner.RandomizedMovementPause(profile, new Random(17));
    TimeSpan routePause = MovementPlanner.RandomizedRouteFollowTransitionPause(profile, new Random(17));

    Assert(routeDuration > TimeSpan.FromMilliseconds(80), "Route follow should still hold keys long enough to look continuous");
    Assert(routeDuration < TimeSpan.FromMilliseconds(420), "Route follow key holds should stay short enough to avoid giant overshooting");
    Assert(routePause < genericPause, "Route follow should use shorter pauses between steps");
}

static void TestEngineTransitions()
{
    RecordingLogger logger = new();
    FakeInputStrategy input = new();
    FakeWindowLocator locator = new();
    AntiAfkEngine engine = new([input], locator, logger, new Random(7));

    List<EngineStatus> statuses = [];
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
    engine.StatusChanged += (_, args) => statuses.Add(args.Status);

    engine.StartAsync(ProfileSettings.CreateDefault(), cts.Token).GetAwaiter().GetResult();
    Task.Delay(350, cts.Token).GetAwaiter().GetResult();
    engine.PauseAsync().GetAwaiter().GetResult();
    engine.ResumeAsync().GetAwaiter().GetResult();
    engine.StopAsync().GetAwaiter().GetResult();

    Assert(statuses.Contains(EngineStatus.Running), "Engine should reach running state");
    Assert(engine.Status == EngineStatus.Stopped, "Engine should stop");
    Assert(input.KeyPressCount > 0, "Engine should emit input");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class RecordingLogger : IAppLogger
{
    public List<string> Messages { get; } = [];

    public void Log(LogSeverity severity, string message) => Messages.Add($"[{severity}] {message}");
}

file sealed class FakeInputStrategy : IInputStrategy
{
    public InputStrategyType StrategyType => InputStrategyType.WindowMessage;

    public int KeyPressCount { get; private set; }

    public Task SendChordAsync(WindowDescriptor window, IReadOnlyCollection<VirtualKey> keys, TimeSpan duration, CancellationToken cancellationToken)
    {
        KeyPressCount += keys.Count;
        return Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }

    public Task SendKeyPressAsync(WindowDescriptor window, VirtualKey key, TimeSpan duration, CancellationToken cancellationToken)
    {
        KeyPressCount++;
        return Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }
}

file sealed class FakeWindowLocator : IWindowLocator
{
    public Task<WindowDescriptor?> FindWindowAsync(CancellationToken cancellationToken) =>
        Task.FromResult<WindowDescriptor?>(new WindowDescriptor((nint)123, 1, "VALORANT", "VALORANT-Win64-Shipping"));

    public Task<bool> IsWindowAvailableAsync(WindowDescriptor descriptor, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
