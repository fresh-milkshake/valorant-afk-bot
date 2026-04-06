using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public sealed class AntiAfkEngine : IAntiAfkEngine
{
    private static readonly IReadOnlyList<VirtualKey> RandomActions =
    [
        VirtualKey.Shift,
        VirtualKey.Control,
        VirtualKey.Space,
    ];

    private readonly Dictionary<InputStrategyType, IInputStrategy> _strategies;
    private readonly IWindowLocator _windowLocator;
    private readonly IAppLogger _logger;
    private readonly Random _random;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private ProfileSettings? _activeProfile;
    private EngineStatus _status = EngineStatus.Stopped;
    private string _lastStatusMessage = "Stopped";
    private bool _paused;
    private int _routePatternIndex;

    public AntiAfkEngine(
        IEnumerable<IInputStrategy> strategies,
        IWindowLocator windowLocator,
        IAppLogger logger,
        Random? random = null)
    {
        _strategies = strategies.ToDictionary(strategy => strategy.StrategyType);
        _windowLocator = windowLocator;
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    public EngineStatus Status => _status;

    public ProfileSettings? ActiveProfile => _activeProfile;

    public event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;

    public async Task StartAsync(ProfileSettings profile, CancellationToken cancellationToken = default)
    {
        ProfileSettings normalized = ProfileSettingsNormalizer.Normalize(profile);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runTask is not null && !_runTask.IsCompleted)
            {
                _activeProfile = normalized;
                return;
            }

            _activeProfile = normalized;
            _paused = false;
            _routePatternIndex = 0;
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetStatus(EngineStatus.WaitingForGame, $"Waiting for VALORANT window for profile '{normalized.Name}'");
            _runTask = RunLoopAsync(_runCts.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync()
    {
        Task? taskToAwait = null;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_runCts is null)
            {
                SetStatus(EngineStatus.Stopped, "Stopped");
                return;
            }

            _runCts.Cancel();
            taskToAwait = _runTask;
        }
        finally
        {
            _stateLock.Release();
        }

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _runTask = null;
            _runCts?.Dispose();
            _runCts = null;
            _paused = false;
            _routePatternIndex = 0;
            SetStatus(EngineStatus.Stopped, "Stopped");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public Task PauseAsync()
    {
        if (_status == EngineStatus.Running)
        {
            _paused = true;
            SetStatus(EngineStatus.Paused, "Paused");
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_status == EngineStatus.Paused)
        {
            _paused = false;
            SetStatus(EngineStatus.WaitingForGame, "Resuming");
        }

        return Task.CompletedTask;
    }

    public void UpdateProfile(ProfileSettings profile)
    {
        _activeProfile = ProfileSettingsNormalizer.Normalize(profile);
        _routePatternIndex = 0;
        _logger.Log(LogSeverity.Info, $"Profile updated: {_activeProfile.Name}");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ProfileSettings profile = _activeProfile ?? ProfileSettings.CreateDefault();
                WindowDescriptor? window = await _windowLocator.FindWindowAsync(cancellationToken).ConfigureAwait(false);
                if (window is null)
                {
                    SetStatus(EngineStatus.WaitingForGame, "Waiting for VALORANT window");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                SetStatus(EngineStatus.Running, $"Running profile '{profile.Name}'");
                IInputStrategy strategy = _strategies[profile.InputStrategy];

                if (profile.Mode == AntiAfkMode.Jumping)
                {
                    await ExecuteJumpingIterationAsync(strategy, window, profile, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await ExecuteWasdIterationAsync(strategy, window, profile, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetStatus(EngineStatus.Error, $"Engine failure: {exception.Message}");
        }
    }

    private async Task ExecuteJumpingIterationAsync(
        IInputStrategy strategy,
        WindowDescriptor window,
        ProfileSettings profile,
        CancellationToken cancellationToken)
    {
        await strategy.SendKeyPressAsync(window, VirtualKey.Space, TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);

        if (_random.NextDouble() < profile.ActionProbability)
        {
            VirtualKey action = RandomActions[_random.Next(RandomActions.Count)];
            await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(60, 200)), cancellationToken).ConfigureAwait(false);
            await strategy.SendKeyPressAsync(window, action, TimeSpan.FromMilliseconds(_random.Next(80, 180)), cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(MovementPlanner.RandomizedJumpDelay(profile, _random), cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteWasdIterationAsync(
        IInputStrategy strategy,
        WindowDescriptor window,
        ProfileSettings profile,
        CancellationToken cancellationToken)
    {
        bool strictRouteFollowing = profile.PatternType == MovementPattern.RouteCanvas;
        if (!strictRouteFollowing && _random.NextDouble() < profile.PauseFrequency)
        {
            await Task.Delay(MovementPlanner.RandomizedIdlePause(profile, _random), cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<string> pattern = MovementPlanner.BuildPattern(profile, _random);
        string direction = profile.PatternType == MovementPattern.RouteCanvas
            ? GetNextRouteDirection(pattern)
            : pattern[_random.Next(pattern.Count)];
        List<VirtualKey> movementKeys = [];

        if (direction.Contains('W'))
        {
            movementKeys.Add(VirtualKey.W);
        }

        if (direction.Contains('A'))
        {
            movementKeys.Add(VirtualKey.A);
        }

        if (direction.Contains('S'))
        {
            movementKeys.Add(VirtualKey.S);
        }

        if (direction.Contains('D'))
        {
            movementKeys.Add(VirtualKey.D);
        }

        TimeSpan duration = strictRouteFollowing
            ? MovementPlanner.RandomizedRouteFollowKeyPress(profile, _random)
            : MovementPlanner.RandomizedKeyPress(profile, _random);
        if (movementKeys.Count == 1)
        {
            await strategy.SendKeyPressAsync(window, movementKeys[0], duration, cancellationToken).ConfigureAwait(false);
        }
        else if (movementKeys.Count > 1)
        {
            await strategy.SendChordAsync(window, movementKeys, duration, cancellationToken).ConfigureAwait(false);
        }

        if (!strictRouteFollowing && _random.NextDouble() < profile.ActionProbability)
        {
            VirtualKey action = _random.NextDouble() switch
            {
                < 0.5 => VirtualKey.Space,
                < 0.75 => VirtualKey.Shift,
                _ => VirtualKey.Control,
            };

            await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(50, 120)), cancellationToken).ConfigureAwait(false);
            await strategy.SendKeyPressAsync(window, action, TimeSpan.FromMilliseconds(_random.Next(90, 180)), cancellationToken).ConfigureAwait(false);
        }

        TimeSpan pause = strictRouteFollowing
            ? MovementPlanner.RandomizedRouteFollowTransitionPause(profile, _random)
            : MovementPlanner.RandomizedMovementPause(profile, _random);
        await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
    }

    private string GetNextRouteDirection(IReadOnlyList<string> pattern)
    {
        if (pattern.Count == 0)
        {
            return "W";
        }

        string direction = pattern[_routePatternIndex % pattern.Count];
        _routePatternIndex = (_routePatternIndex + 1) % pattern.Count;
        return direction;
    }

    private void SetStatus(EngineStatus status, string message)
    {
        if (_status == status && string.Equals(_lastStatusMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        _lastStatusMessage = message;
        _logger.Log(status == EngineStatus.Error ? LogSeverity.Error : LogSeverity.Info, message);
        StatusChanged?.Invoke(this, new EngineStatusChangedEventArgs(status, message));
    }
}
