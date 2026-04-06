using System.Collections.ObjectModel;
using ValorantAfkBot.App.Commands;
using ValorantAfkBot.App.Services;
using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Core.Services;

namespace ValorantAfkBot.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const string MissingValorantMessage = "VALORANT process/window not found. Start the game first, then press Start again.";

    private readonly IAntiAfkEngine _engine;
    private readonly IProfileRepository _repository;
    private readonly IAutostartService _autostartService;
    private readonly IWindowLocator _windowLocator;
    private readonly ObservableLogService _logger;
    private readonly string _executablePath;
    private readonly string _dataDirectoryPath;
    private readonly string _configFilePath;
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string ValorantProcessNameHint = "VALORANT-Win64-Shipping";

    private ProfileSettings? _selectedProfile;
    private bool _suspendProfileUpdates;
    private string _statusText = "Stopped";
    private string _statusDetail = "Stopped";
    private bool _launchOnStartup;
    private bool _minimizeToTrayOnClose = true;
    private bool _persistLogsToDisk = true;
    private int _maxLogEntries = 500;
    private string _profileName = string.Empty;
    private AntiAfkMode _selectedMode;
    private InputStrategyType _selectedInputStrategy;
    private double _jumpDelaySeconds;
    private double _keyPressDelaySeconds;
    private string _movementPath = "WASD";
    private IReadOnlyList<RouteCanvasPoint> _routeCanvasPoints = ProfileSettings.CreateDefaultRouteCanvas();
    private MovementPattern _patternType;
    private double _movementIntensity;
    private double _directionChangeFrequency;
    private double _actionProbability;
    private double _strafePreference;
    private double _movementSmoothness;
    private double _pauseFrequency;
    private double _minPauseSeconds;
    private double _maxPauseSeconds;
    private string _startHotkeyText = "Ctrl+Alt+F9";
    private string _stopHotkeyText = "Ctrl+Alt+F10";
    private string _pauseHotkeyText = "Ctrl+Alt+F11";
    private bool _isWindowSearchRunning;
    private bool _hasDetectedWindow;
    private string _windowSearchSummary = "Not checked yet.";
    private string _detectedWindowTitle = "Not found";
    private string _detectedProcessName = "Not found";
    private string _detectedProcessId = "-";
    private string _detectedWindowHandle = "-";

    public MainViewModel(
        IAntiAfkEngine engine,
        IProfileRepository repository,
        IAutostartService autostartService,
        IWindowLocator windowLocator,
        ObservableLogService logger,
        string executablePath,
        string dataDirectoryPath,
        string configFilePath,
        string logFilePath)
    {
        _engine = engine;
        _repository = repository;
        _autostartService = autostartService;
        _windowLocator = windowLocator;
        _logger = logger;
        _executablePath = executablePath;
        _dataDirectoryPath = dataDirectoryPath;
        _configFilePath = configFilePath;
        _logFilePath = logFilePath;

        Profiles = [];
        ModeOptions = Enum.GetValues<AntiAfkMode>();
        InputStrategyOptions = Enum.GetValues<InputStrategyType>();
        PatternOptions = Enum.GetValues<MovementPattern>();

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        PauseResumeCommand = new AsyncRelayCommand(TogglePauseAsync, () => CanPause);
        RefreshWindowProbeCommand = new AsyncRelayCommand(RefreshWindowProbeAsync, () => !IsWindowSearchRunning);
        CreateProfileCommand = new RelayCommand(CreateProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile, () => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1 && SelectedProfile is not null);
        ApplyHotkeysCommand = new RelayCommand(ApplyHotkeys);

        _engine.StatusChanged += (_, args) =>
        {
            StatusText = args.Status switch
            {
                EngineStatus.WaitingForGame => "WaitingForGame",
                EngineStatus.Running => "Running",
                EngineStatus.Paused => "Paused",
                EngineStatus.Error => "Error",
                _ => "Stopped",
            };
            StatusDetail = args.Message;
        };
    }

    public ObservableCollection<ProfileSettings> Profiles { get; }

    public IReadOnlyList<AntiAfkMode> ModeOptions { get; }

    public IReadOnlyList<InputStrategyType> InputStrategyOptions { get; }

    public IReadOnlyList<MovementPattern> PatternOptions { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand PauseResumeCommand { get; }

    public RelayCommand CreateProfileCommand { get; }

    public RelayCommand DuplicateProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public RelayCommand ApplyHotkeysCommand { get; }

    public AsyncRelayCommand RefreshWindowProbeCommand { get; }

    public event Action? HotkeysApplied;

    public IReadOnlyList<string> GetLogs() => _logger.GetSnapshot();

    public IReadOnlyList<LogEntry> GetLogEntries() => _logger.GetEntriesSnapshot();

    public void ClearLogs() => _logger.Clear();

    public ProfileSettings? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value) && value is not null)
            {
                ApplyProfile(value);
                QueueSave();
                RefreshCommands();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(PauseResumeText));
                RefreshCommands();
            }
        }
    }

    public bool CanStart => StatusText is "Stopped" or "Error" or "WaitingForGame";

    public bool CanStop => StatusText is not "Stopped";

    public bool CanPause => StatusText is "Running" or "Paused";

    public string PauseResumeText => StatusText == "Paused" ? "Resume" : "Pause";

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public bool ShowJumpingSettings => SelectedMode == AntiAfkMode.Jumping;

    public bool ShowWasdSettings => SelectedMode is AntiAfkMode.Wasd or AntiAfkMode.PathFollow;

    public bool IsPathFollowMode => SelectedMode == AntiAfkMode.PathFollow;

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        set
        {
            if (SetProperty(ref _launchOnStartup, value))
            {
                _ = UpdateAutostartAsync(value);
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                QueueSave();
            }
        }
    }

    public bool PersistLogsToDisk
    {
        get => _persistLogsToDisk;
        set
        {
            if (SetProperty(ref _persistLogsToDisk, value))
            {
                _logger.Configure(MaxLogEntries, value);
                QueueSave();
            }
        }
    }

    public int MaxLogEntries
    {
        get => _maxLogEntries;
        set
        {
            if (SetProperty(ref _maxLogEntries, value))
            {
                _logger.Configure(value, PersistLogsToDisk);
                QueueSave();
            }
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value))
            {
                UpdateSelectedProfile(profile => profile with { Name = value });
            }
        }
    }

    public AntiAfkMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(ShowJumpingSettings));
                OnPropertyChanged(nameof(ShowWasdSettings));
                OnPropertyChanged(nameof(IsPathFollowMode));

                MovementPattern synchronizedPattern = ResolvePatternForMode(value, _patternType);
                if (_patternType != synchronizedPattern)
                {
                    _patternType = synchronizedPattern;
                    OnPropertyChanged(nameof(PatternType));
                }

                UpdateSelectedProfile(profile => profile with
                {
                    Mode = value,
                    PatternType = synchronizedPattern,
                });
            }
        }
    }

    public InputStrategyType SelectedInputStrategy
    {
        get => _selectedInputStrategy;
        set
        {
            if (SetProperty(ref _selectedInputStrategy, value))
            {
                UpdateSelectedProfile(profile => profile with { InputStrategy = value });
            }
        }
    }

    public double JumpDelaySeconds
    {
        get => _jumpDelaySeconds;
        set
        {
            if (SetProperty(ref _jumpDelaySeconds, value))
            {
                UpdateSelectedProfile(profile => profile with { JumpDelaySeconds = value });
            }
        }
    }

    public double KeyPressDelaySeconds
    {
        get => _keyPressDelaySeconds;
        set
        {
            if (SetProperty(ref _keyPressDelaySeconds, value))
            {
                UpdateSelectedProfile(profile => profile with { KeyPressDelaySeconds = value });
            }
        }
    }

    public string MovementPath
    {
        get => _movementPath;
        set
        {
            if (SetProperty(ref _movementPath, value))
            {
                UpdateSelectedProfile(profile => profile with { MovementPath = value });
            }
        }
    }

    public IReadOnlyList<RouteCanvasPoint> RouteCanvasPoints => _routeCanvasPoints;

    public MovementPattern PatternType
    {
        get => _patternType;
        set
        {
            if (SetProperty(ref _patternType, value))
            {
                AntiAfkMode synchronizedMode = ResolveModeForPattern(_selectedMode, value);
                if (_selectedMode != synchronizedMode)
                {
                    _selectedMode = synchronizedMode;
                    OnPropertyChanged(nameof(SelectedMode));
                    OnPropertyChanged(nameof(ShowJumpingSettings));
                    OnPropertyChanged(nameof(ShowWasdSettings));
                    OnPropertyChanged(nameof(IsPathFollowMode));
                }

                UpdateSelectedProfile(profile => profile with
                {
                    PatternType = value,
                    Mode = synchronizedMode,
                });
            }
        }
    }

    public double MovementIntensity
    {
        get => _movementIntensity;
        set
        {
            if (SetProperty(ref _movementIntensity, value))
            {
                UpdateSelectedProfile(profile => profile with { MovementIntensity = value });
            }
        }
    }

    public double DirectionChangeFrequency
    {
        get => _directionChangeFrequency;
        set
        {
            if (SetProperty(ref _directionChangeFrequency, value))
            {
                UpdateSelectedProfile(profile => profile with { DirectionChangeFrequency = value });
            }
        }
    }

    public double ActionProbability
    {
        get => _actionProbability;
        set
        {
            if (SetProperty(ref _actionProbability, value))
            {
                UpdateSelectedProfile(profile => profile with { ActionProbability = value });
            }
        }
    }

    public double StrafePreference
    {
        get => _strafePreference;
        set
        {
            if (SetProperty(ref _strafePreference, value))
            {
                UpdateSelectedProfile(profile => profile with { StrafePreference = value });
            }
        }
    }

    public double MovementSmoothness
    {
        get => _movementSmoothness;
        set
        {
            if (SetProperty(ref _movementSmoothness, value))
            {
                UpdateSelectedProfile(profile => profile with { MovementSmoothness = value });
            }
        }
    }

    public double PauseFrequency
    {
        get => _pauseFrequency;
        set
        {
            if (SetProperty(ref _pauseFrequency, value))
            {
                UpdateSelectedProfile(profile => profile with { PauseFrequency = value });
            }
        }
    }

    public double MinPauseSeconds
    {
        get => _minPauseSeconds;
        set
        {
            if (SetProperty(ref _minPauseSeconds, value))
            {
                UpdateSelectedProfile(profile => profile with { MinPauseSeconds = value });
            }
        }
    }

    public double MaxPauseSeconds
    {
        get => _maxPauseSeconds;
        set
        {
            if (SetProperty(ref _maxPauseSeconds, value))
            {
                UpdateSelectedProfile(profile => profile with { MaxPauseSeconds = value });
            }
        }
    }

    public string StartHotkeyText
    {
        get => _startHotkeyText;
        set => SetProperty(ref _startHotkeyText, value);
    }

    public string StopHotkeyText
    {
        get => _stopHotkeyText;
        set => SetProperty(ref _stopHotkeyText, value);
    }

    public string PauseHotkeyText
    {
        get => _pauseHotkeyText;
        set => SetProperty(ref _pauseHotkeyText, value);
    }

    public string ValorantProcessNameHintText => ValorantProcessNameHint;

    public string DataDirectoryPath => _dataDirectoryPath;

    public string ConfigFilePath => _configFilePath;

    public string LogFilePath => _logFilePath;

    public string ExecutablePath => _executablePath;

    public bool IsWindowSearchRunning
    {
        get => _isWindowSearchRunning;
        private set
        {
            if (SetProperty(ref _isWindowSearchRunning, value))
            {
                RefreshWindowProbeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasDetectedWindow
    {
        get => _hasDetectedWindow;
        private set => SetProperty(ref _hasDetectedWindow, value);
    }

    public string WindowSearchSummary
    {
        get => _windowSearchSummary;
        private set => SetProperty(ref _windowSearchSummary, value);
    }

    public string DetectedWindowTitle
    {
        get => _detectedWindowTitle;
        private set => SetProperty(ref _detectedWindowTitle, value);
    }

    public string DetectedProcessName
    {
        get => _detectedProcessName;
        private set => SetProperty(ref _detectedProcessName, value);
    }

    public string DetectedProcessId
    {
        get => _detectedProcessId;
        private set => SetProperty(ref _detectedProcessId, value);
    }

    public string DetectedWindowHandle
    {
        get => _detectedWindowHandle;
        private set => SetProperty(ref _detectedWindowHandle, value);
    }

    public void SetRouteCanvasPoints(IReadOnlyList<RouteCanvasPoint> points)
    {
        List<RouteCanvasPoint> normalized = ProfileSettingsNormalizer.Normalize(
            (SelectedProfile ?? ProfileSettings.CreateDefault()) with { RouteCanvasPoints = points }).RouteCanvasPoints.ToList();

        if (_routeCanvasPoints.SequenceEqual(normalized))
        {
            return;
        }

        _routeCanvasPoints = normalized;
        OnPropertyChanged(nameof(RouteCanvasPoints));
        UpdateSelectedProfile(profile => profile with { RouteCanvasPoints = normalized });
    }

    public void ResetRouteCanvas() => SetRouteCanvasPoints(ProfileSettings.CreateDefaultRouteCanvas());

    public async Task InitializeAsync()
    {
        ConfigurationDocument document = await _repository.LoadAsync().ConfigureAwait(false);
        _logger.Configure(document.AppSettings.MaxLogEntries, document.AppSettings.PersistLogsToDisk);

        _launchOnStartup = document.AppSettings.LaunchOnStartup || _autostartService.IsEnabled();
        _minimizeToTrayOnClose = document.AppSettings.MinimizeToTrayOnClose;
        _persistLogsToDisk = document.AppSettings.PersistLogsToDisk;
        _maxLogEntries = document.AppSettings.MaxLogEntries;
        OnPropertyChanged(nameof(LaunchOnStartup));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(PersistLogsToDisk));
        OnPropertyChanged(nameof(MaxLogEntries));

        Profiles.Clear();
        foreach (ProfileSettings profile in document.Profiles)
        {
            Profiles.Add(profile);
        }

        StartHotkeyText = HotkeyBindingFormatter.ToDisplayString(FindHotkey(document.AppSettings.Hotkeys, HotkeyAction.Start));
        StopHotkeyText = HotkeyBindingFormatter.ToDisplayString(FindHotkey(document.AppSettings.Hotkeys, HotkeyAction.Stop));
        PauseHotkeyText = HotkeyBindingFormatter.ToDisplayString(FindHotkey(document.AppSettings.Hotkeys, HotkeyAction.PauseResume));

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == document.AppSettings.LastProfileId) ?? Profiles.First();
        StatusText = "Stopped";
        StatusDetail = "Stopped";
        await RefreshWindowProbeAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<HotkeyBinding> GetHotkeyBindings()
    {
        List<HotkeyBinding> bindings = [];
        AddHotkey(bindings, StartHotkeyText, HotkeyAction.Start, "Ctrl+Alt+F9");
        AddHotkey(bindings, StopHotkeyText, HotkeyAction.Stop, "Ctrl+Alt+F10");
        AddHotkey(bindings, PauseHotkeyText, HotkeyAction.PauseResume, "Ctrl+Alt+F11");
        return bindings;
    }

    private async Task StartAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        WindowDescriptor? window = await _windowLocator.FindWindowAsync(CancellationToken.None).ConfigureAwait(false);
        if (window is null)
        {
            UpdateDetectedWindow(null);
            StatusText = "Error";
            StatusDetail = MissingValorantMessage;
            _logger.Log(LogSeverity.Error, MissingValorantMessage);
            return;
        }

        UpdateDetectedWindow(window);
        await _engine.StartAsync(SelectedProfile).ConfigureAwait(false);
    }

    private async Task StopAsync()
    {
        await _engine.StopAsync().ConfigureAwait(false);
    }

    private async Task TogglePauseAsync()
    {
        if (StatusText == "Paused")
        {
            await _engine.ResumeAsync().ConfigureAwait(false);
        }
        else
        {
            await _engine.PauseAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshWindowProbeAsync()
    {
        IsWindowSearchRunning = true;
        try
        {
            WindowDescriptor? window = await _windowLocator.FindWindowAsync(CancellationToken.None).ConfigureAwait(false);
            UpdateDetectedWindow(window);
        }
        finally
        {
            IsWindowSearchRunning = false;
        }
    }

    private void CreateProfile()
    {
        ProfileSettings profile = ProfileSettings.CreateDefault() with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Profile {Profiles.Count + 1}",
        };
        Profiles.Add(profile);
        SelectedProfile = profile;
        QueueSave();
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        ProfileSettings clone = SelectedProfile with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{SelectedProfile.Name} Copy",
        };
        Profiles.Add(clone);
        SelectedProfile = clone;
        QueueSave();
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1)
        {
            return;
        }

        int currentIndex = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(currentIndex - 1, 0, Profiles.Count - 1)];
        QueueSave();
    }

    private void ApplyHotkeys()
    {
        QueueSave();
        HotkeysApplied?.Invoke();
    }

    private void ApplyProfile(ProfileSettings profile)
    {
        _suspendProfileUpdates = true;
        try
        {
            ProfileName = profile.Name;
            SelectedMode = profile.Mode;
            SelectedInputStrategy = profile.InputStrategy;
            JumpDelaySeconds = profile.JumpDelaySeconds;
            KeyPressDelaySeconds = profile.KeyPressDelaySeconds;
            MovementPath = profile.MovementPath;
            _routeCanvasPoints = profile.RouteCanvasPoints.ToList();
            OnPropertyChanged(nameof(RouteCanvasPoints));
            PatternType = profile.PatternType;
            MovementIntensity = profile.MovementIntensity;
            DirectionChangeFrequency = profile.DirectionChangeFrequency;
            ActionProbability = profile.ActionProbability;
            StrafePreference = profile.StrafePreference;
            MovementSmoothness = profile.MovementSmoothness;
            PauseFrequency = profile.PauseFrequency;
            MinPauseSeconds = profile.MinPauseSeconds;
            MaxPauseSeconds = profile.MaxPauseSeconds;
        }
        finally
        {
            _suspendProfileUpdates = false;
        }
    }

    private void UpdateSelectedProfile(Func<ProfileSettings, ProfileSettings> updater)
    {
        if (_suspendProfileUpdates || SelectedProfile is null)
        {
            return;
        }

        ProfileSettings normalized = ProfileSettingsNormalizer.Normalize(updater(SelectedProfile));
        int index = Profiles.IndexOf(SelectedProfile);
        Profiles[index] = normalized;
        _selectedProfile = normalized;
        OnPropertyChanged(nameof(SelectedProfile));
        _engine.UpdateProfile(normalized);
        QueueSave();
    }

    private async Task UpdateAutostartAsync(bool enabled)
    {
        await _autostartService.SetEnabledAsync(enabled, _executablePath).ConfigureAwait(false);
        QueueSave();
    }

    private void QueueSave() => _ = SaveAsync();

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Profiles.Count == 0)
            {
                return;
            }

            ConfigurationDocument document = new()
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                AppSettings = new AppSettings
                {
                    SchemaVersion = AppSettings.CurrentSchemaVersion,
                    LaunchOnStartup = LaunchOnStartup,
                    LastProfileId = SelectedProfile?.Id ?? Profiles[0].Id,
                    MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                    PersistLogsToDisk = PersistLogsToDisk,
                    MaxLogEntries = MaxLogEntries,
                    Hotkeys = GetHotkeyBindings(),
                },
                Profiles = Profiles.Select(ProfileSettingsNormalizer.Normalize).ToList(),
            };

            await _repository.SaveAsync(document).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void AddHotkey(List<HotkeyBinding> bindings, string text, HotkeyAction action, string fallbackText)
    {
        if (!HotkeyBindingFormatter.TryParse(text, action, out HotkeyBinding? parsed) &&
            !HotkeyBindingFormatter.TryParse(fallbackText, action, out parsed))
        {
            return;
        }

        bindings.Add(parsed!);
    }

    private static HotkeyBinding FindHotkey(IEnumerable<HotkeyBinding> hotkeys, HotkeyAction action) =>
        hotkeys.FirstOrDefault(binding => binding.Action == action)
        ?? HotkeyBinding.CreateDefaults().First(binding => binding.Action == action);

    private void RefreshCommands()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        DuplicateProfileCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
    }

    private void UpdateDetectedWindow(WindowDescriptor? window)
    {
        if (window is null)
        {
            HasDetectedWindow = false;
            WindowSearchSummary = "Visible VALORANT game window not detected.";
            DetectedWindowTitle = "Not found";
            DetectedProcessName = "Not found";
            DetectedProcessId = "-";
            DetectedWindowHandle = "-";
            return;
        }

        HasDetectedWindow = true;
        WindowSearchSummary = "VALORANT process detected with a visible game window.";
        DetectedWindowTitle = window.Title;
        DetectedProcessName = window.ProcessName ?? ValorantProcessNameHint;
        DetectedProcessId = window.ProcessId.ToString();
        DetectedWindowHandle = $"0x{window.Handle.ToInt64():X}";
    }

    private static MovementPattern ResolvePatternForMode(AntiAfkMode mode, MovementPattern currentPattern) => mode switch
    {
        AntiAfkMode.PathFollow => MovementPattern.RouteCanvas,
        AntiAfkMode.Wasd when currentPattern == MovementPattern.RouteCanvas => MovementPattern.Random,
        _ => currentPattern,
    };

    private static AntiAfkMode ResolveModeForPattern(AntiAfkMode currentMode, MovementPattern pattern) => pattern switch
    {
        MovementPattern.RouteCanvas => AntiAfkMode.PathFollow,
        _ when currentMode == AntiAfkMode.PathFollow => AntiAfkMode.Wasd,
        _ => currentMode,
    };
}
