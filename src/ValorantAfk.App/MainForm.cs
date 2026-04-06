using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using ImGuiNET;
using OpenTK.GLControl;
using ValorantAfkBot.App.ImGuiSupport;
using ValorantAfkBot.App.ViewModels;
using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;
using ValorantAfkBot.Core.Models;
using ValorantAfkBot.Windows.Hotkeys;
using ValorantAfkBot.Windows.SingleInstance;
using ValorantAfkBot.Windows.Tray;

namespace ValorantAfkBot.App;

public sealed class MainForm : Form
{
    private const float RouteCanvasWorldMin = -0.5f;
    private const float RouteCanvasWorldMax = 1.5f;
    private readonly MainViewModel _viewModel;
    private readonly TrayIconController _trayIconController;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly SingleInstanceCoordinator _singleInstanceCoordinator;
    private readonly GLControl _glControl;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    private static readonly Vector2 DefaultRouteCanvasViewCenter = new(0.5f, 0.5f);

    private ImGuiController? _imGuiController;
    private double _lastFrameSeconds;
    private bool _allowClose;
    private bool _autoScrollLogs = true;
    private string _logSearchText = string.Empty;
    private bool _showDebugLogs = true;
    private bool _showInfoLogs = true;
    private bool _showWarningLogs = true;
    private bool _showErrorLogs = true;
    private string? _selectedLogText;
    private string _selectedLogTimestamp = "-";
    private string _selectedLogSeverity = "-";
    private string _selectedLogMessage = "Select a log row to inspect it here.";
    private readonly List<RouteCanvasPoint> _routeCanvasDraft = [];
    private string? _routeCanvasDraftProfileId;
    private bool _isRouteCanvasDrawing;
    private bool _isRouteCanvasPanning;
    private float _routeCanvasZoom = 1f;
    private Vector2 _routeCanvasViewCenter = DefaultRouteCanvasViewCenter;

    public MainForm(
        MainViewModel viewModel,
        TrayIconController trayIconController,
        GlobalHotkeyManager hotkeyManager,
        SingleInstanceCoordinator singleInstanceCoordinator,
        Icon icon)
    {
        _viewModel = viewModel;
        _trayIconController = trayIconController;
        _hotkeyManager = hotkeyManager;
        _singleInstanceCoordinator = singleInstanceCoordinator;

        Text = "Valorant AFK Bot";
        Icon = (Icon)icon.Clone();
        Width = 960;
        Height = 650;
        MinimumSize = new Size(760, 540);
        StartPosition = FormStartPosition.CenterScreen;

        _glControl = new GLControl
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            TabStop = true,
        };
        Controls.Add(_glControl);

        _renderTimer = new System.Windows.Forms.Timer
        {
            Interval = 16,
            Enabled = true,
        };
        _renderTimer.Tick += (_, _) => _glControl.Invalidate();

        _glControl.Load += OnGlLoad;
        _glControl.Paint += OnGlPaint;
        _glControl.Resize += OnGlResize;

        FormClosing += OnFormClosing;
        Shown += (_, _) => UpdateTrayState();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.HotkeysApplied += RegisterHotkeys;

        _trayIconController.OpenRequested += ShowFromTray;
        _trayIconController.StartRequested += () => _viewModel.StartCommand.Execute(null);
        _trayIconController.StopRequested += () => _viewModel.StopCommand.Execute(null);
        _trayIconController.PauseResumeRequested += () => _viewModel.PauseResumeCommand.Execute(null);
        _trayIconController.ExitRequested += ExitApplication;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyManager.Attach(Handle);
        RegisterHotkeys();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Dispose();
            _imGuiController?.Dispose();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _trayIconController.OpenRequested -= ShowFromTray;
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkeyManager.HotkeyMessageId &&
            _hotkeyManager.TryGetAction(m.WParam.ToInt32(), out HotkeyAction action))
        {
            HandleHotkey(action);
            return;
        }

        if ((uint)m.Msg == _singleInstanceCoordinator.ActivateMessageId)
        {
            ShowFromTray();
            return;
        }

        base.WndProc(ref m);
    }

    private void OnGlLoad(object? sender, EventArgs e)
    {
        _glControl.MakeCurrent();
        _imGuiController = new ImGuiController(_glControl.ClientSize.Width, _glControl.ClientSize.Height);
        _imGuiController.Bind(_glControl);
        _glControl.Focus();
    }

    private void OnGlResize(object? sender, EventArgs e)
    {
        _imGuiController?.WindowResized(_glControl.ClientSize.Width, _glControl.ClientSize.Height);
    }

    private void OnGlPaint(object? sender, PaintEventArgs e)
    {
        if (_imGuiController is null)
        {
            return;
        }

        double now = _frameTimer.Elapsed.TotalSeconds;
        float deltaSeconds = _lastFrameSeconds == 0
            ? 1f / 60f
            : (float)Math.Clamp(now - _lastFrameSeconds, 1d / 240d, 0.1d);
        _lastFrameSeconds = now;

        _imGuiController.Update(_glControl, deltaSeconds);
        RenderUi();
        _imGuiController.Render();
        _glControl.SwapBuffers();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
        Application.Exit();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        _glControl.Focus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.PauseResumeText))
        {
            UpdateTrayState();
        }
    }

    private void UpdateTrayState() =>
        _trayIconController.UpdateStatus(_viewModel.StatusText, _viewModel.CanPause);

    private void RegisterHotkeys()
    {
        _ = _hotkeyManager.Register(_viewModel.GetHotkeyBindings());
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Start:
                _viewModel.StartCommand.Execute(null);
                break;
            case HotkeyAction.Stop:
                _viewModel.StopCommand.Execute(null);
                break;
            case HotkeyAction.PauseResume:
                _viewModel.PauseResumeCommand.Execute(null);
                break;
        }
    }

    private void RenderUi()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags shellFlags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.MenuBar;

        if (!ImGui.Begin($"{Text}###AppShell", shellFlags))
        {
            ImGui.End();
            return;
        }

        RenderShellMenuBar();
        RenderShellHeader();

        if (ImGui.BeginTabBar("app-tabs"))
        {
            if (ImGui.BeginTabItem("Overview"))
            {
                RenderOverviewPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Profile"))
            {
                RenderProfilePage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Behavior"))
            {
                RenderBehaviorPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Hotkeys"))
            {
                RenderHotkeysPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Logs"))
            {
                RenderLogsPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Diagnostics"))
            {
                RenderDiagnosticsPage();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void RenderShellMenuBar()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("Actions"))
        {
            if (ImGui.MenuItem("Start", _viewModel.StartHotkeyText, false, _viewModel.CanStart))
            {
                _viewModel.StartCommand.Execute(null);
            }

            if (ImGui.MenuItem("Stop", _viewModel.StopHotkeyText, false, _viewModel.CanStop))
            {
                _viewModel.StopCommand.Execute(null);
            }

            if (ImGui.MenuItem(_viewModel.PauseResumeText, _viewModel.PauseHotkeyText, false, _viewModel.CanPause))
            {
                _viewModel.PauseResumeCommand.Execute(null);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Exit"))
            {
                ExitApplication();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Profiles"))
        {
            if (ImGui.MenuItem("New"))
            {
                _viewModel.CreateProfileCommand.Execute(null);
            }

            if (ImGui.MenuItem("Duplicate", string.Empty, false, _viewModel.SelectedProfile is not null))
            {
                _viewModel.DuplicateProfileCommand.Execute(null);
            }

            if (ImGui.MenuItem("Delete", string.Empty, false, _viewModel.Profiles.Count > 1 && _viewModel.SelectedProfile is not null))
            {
                _viewModel.DeleteProfileCommand.Execute(null);
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            ImGui.MenuItem("Default Dear ImGui style", string.Empty, true, false);
            ImGui.MenuItem("Docking enabled", string.Empty, true, false);
            ImGui.MenuItem("Keyboard navigation enabled", string.Empty, true, false);
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void RenderShellHeader()
    {
        float availableWidth = ImGui.GetContentRegionAvail().X;
        bool compact = availableWidth < 760f;

        if (compact)
        {
            ImGui.TextUnformatted($"Status: {_viewModel.StatusText}");
            ImGui.TextDisabled(_viewModel.StatusDetail);
            ImGui.TextDisabled($"Profile: {_viewModel.SelectedProfile?.Name ?? "<none>"}");
            ImGui.TextDisabled($"Mode: {FormatEnumLabel(_viewModel.SelectedMode)} | Input: {FormatEnumLabel(_viewModel.SelectedInputStrategy)}");
        }
        else if (ImGui.BeginTable("shell-header", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"Status: {_viewModel.StatusText}");
            ImGui.TextDisabled(_viewModel.StatusDetail);
            ImGui.TextDisabled($"Profile: {_viewModel.SelectedProfile?.Name ?? "<none>"}");
            ImGui.TextDisabled($"Mode: {FormatEnumLabel(_viewModel.SelectedMode)} | Input: {FormatEnumLabel(_viewModel.SelectedInputStrategy)}");

            ImGui.TableNextColumn();
            float buttonWidth = MathF.Max(96f, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f);
            Vector2 buttonSize = new(buttonWidth, 0f);
            if (RenderCommandButton("Start", _viewModel.CanStart, buttonSize))
            {
                _viewModel.StartCommand.Execute(null);
            }

            ImGui.SameLine();
            if (RenderCommandButton("Stop", _viewModel.CanStop, buttonSize))
            {
                _viewModel.StopCommand.Execute(null);
            }

            ImGui.SameLine();
            if (RenderCommandButton(_viewModel.PauseResumeText, _viewModel.CanPause, buttonSize))
            {
                _viewModel.PauseResumeCommand.Execute(null);
            }

            ImGui.EndTable();
        }

        if (compact)
        {
            float buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
            buttonWidth = MathF.Max(84f, buttonWidth);
            Vector2 buttonSize = new(buttonWidth, 0f);
            if (RenderCommandButton("Start", _viewModel.CanStart, buttonSize))
            {
                _viewModel.StartCommand.Execute(null);
            }

            ImGui.SameLine();
            if (RenderCommandButton("Stop", _viewModel.CanStop, buttonSize))
            {
                _viewModel.StopCommand.Execute(null);
            }

            ImGui.SameLine();
            if (RenderCommandButton(_viewModel.PauseResumeText, _viewModel.CanPause, buttonSize))
            {
                _viewModel.PauseResumeCommand.Execute(null);
            }
        }

        ImGui.Separator();
    }

    private void RenderOverviewPage()
    {
        if (!ImGui.BeginChild("overview-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        bool wide = ImGui.GetContentRegionAvail().X >= 900f;
        if (wide && ImGui.BeginTable("overview-layout", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextColumn();
            RenderOverviewLeftColumn();
            ImGui.TableNextColumn();
            RenderOverviewRightColumn();
            ImGui.EndTable();
        }
        else
        {
            RenderOverviewLeftColumn();
            ImGui.Separator();
            RenderOverviewRightColumn();
        }

        ImGui.EndChild();
    }

    private void RenderOverviewLeftColumn()
    {
        if (ImGui.CollapsingHeader("Session", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("overview-session", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Status", _viewModel.StatusText);
            RenderSummaryRow("Status detail", _viewModel.StatusDetail);
            RenderSummaryRow("Pause action", _viewModel.PauseResumeText);
            RenderSummaryRow("Active profile", _viewModel.SelectedProfile?.Name ?? "<none>");
            RenderSummaryRow("Mode", FormatEnumLabel(_viewModel.SelectedMode));
            RenderSummaryRow("Input", FormatEnumLabel(_viewModel.SelectedInputStrategy));
            ImGui.EndTable();
        }

        if (ImGui.CollapsingHeader("VALORANT Target", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (RenderCommandButton(
                    _viewModel.IsWindowSearchRunning ? "Searching..." : "Refresh target scan",
                    !_viewModel.IsWindowSearchRunning,
                    new Vector2(180f, 0f)))
            {
                _viewModel.RefreshWindowProbeCommand.Execute(null);
            }

            ImGui.SameLine();
            ImGui.TextDisabled(_viewModel.HasDetectedWindow ? "Ready" : "Waiting for game");

            ImGui.TextWrapped(_viewModel.WindowSearchSummary);

            if (ImGui.BeginTable("overview-target", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                RenderSummaryRow("Match rule", "Visible window owned by expected process");
                RenderSummaryRow("Expected process", _viewModel.ValorantProcessNameHintText);
                RenderSummaryRow("Detected title", _viewModel.DetectedWindowTitle);
                RenderSummaryRow("Detected process", _viewModel.DetectedProcessName);
                RenderSummaryRow("PID", _viewModel.DetectedProcessId);
                RenderSummaryRow("Window handle", _viewModel.DetectedWindowHandle);
                ImGui.EndTable();
            }
        }
    }

    private void RenderOverviewRightColumn()
    {
        if (ImGui.CollapsingHeader("Active Profile", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("overview-profile", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Name", _viewModel.SelectedProfile?.Name ?? "<none>");
            RenderSummaryRow("Mode", FormatEnumLabel(_viewModel.SelectedMode));
            RenderSummaryRow("Input", FormatEnumLabel(_viewModel.SelectedInputStrategy));
            RenderSummaryRow("Pattern", FormatEnumLabel(_viewModel.PatternType));

            if (_viewModel.ShowJumpingSettings)
            {
                RenderSummaryRow("Jump delay", $"{_viewModel.JumpDelaySeconds:0.00}s");
            }
            else
            {
                RenderSummaryRow("Movement path", _viewModel.MovementPath);
                RenderSummaryRow("Pause range", $"{_viewModel.MinPauseSeconds:0.00}s - {_viewModel.MaxPauseSeconds:0.00}s");
            }

            if (_viewModel.IsPathFollowMode)
            {
                RenderSummaryRow("Route points", _viewModel.RouteCanvasPoints.Count.ToString());
            }

            ImGui.EndTable();
        }

        if (ImGui.CollapsingHeader("Hotkeys", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("hotkey-summary", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                RenderSummaryRow("Start", _viewModel.StartHotkeyText);
                RenderSummaryRow("Stop", _viewModel.StopHotkeyText);
                RenderSummaryRow("Pause / Resume", _viewModel.PauseHotkeyText);
                ImGui.EndTable();
            }

            if (ImGui.Button("Apply hotkeys"))
            {
                _viewModel.ApplyHotkeysCommand.Execute(null);
            }
        }

        if (ImGui.CollapsingHeader("Application", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool minimizeToTray = _viewModel.MinimizeToTrayOnClose;
            if (ImGui.Checkbox("Minimize to tray on close", ref minimizeToTray))
            {
                _viewModel.MinimizeToTrayOnClose = minimizeToTray;
            }

            bool launchOnStartup = _viewModel.LaunchOnStartup;
            if (ImGui.Checkbox("Launch on Windows startup", ref launchOnStartup))
            {
                _viewModel.LaunchOnStartup = launchOnStartup;
            }

            bool persistLogs = _viewModel.PersistLogsToDisk;
            if (ImGui.Checkbox("Persist logs to disk", ref persistLogs))
            {
                _viewModel.PersistLogsToDisk = persistLogs;
            }
        }
    }

    private void RenderProfilePage()
    {
        if (!ImGui.BeginChild("profile-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        RenderProfileSelector();
        ImGui.Separator();
        RenderResponsiveColumns(
            "profile-layout",
            900f,
            RenderProfilePrimaryColumn,
            RenderProfileSecondaryColumn);
        ImGui.EndChild();
    }

    private void RenderProfileSelector()
    {
        ProfileSettings? selected = _viewModel.SelectedProfile;
        string preview = selected?.Name ?? "<none>";
        ImGui.SetNextItemWidth(-145f);
        if (ImGui.BeginCombo("Active profile", preview))
        {
            foreach (ProfileSettings profile in _viewModel.Profiles)
            {
                bool isSelected = selected?.Id == profile.Id;
                if (ImGui.Selectable(profile.Name, isSelected))
                {
                    _viewModel.SelectedProfile = profile;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("New"))
        {
            _viewModel.CreateProfileCommand.Execute(null);
        }

        ImGui.SameLine();
        if (RenderCommandButton("Duplicate", selected is not null, new Vector2(92f, 0f)))
        {
            _viewModel.DuplicateProfileCommand.Execute(null);
        }

        ImGui.SameLine();
        if (RenderCommandButton("Delete", _viewModel.Profiles.Count > 1 && selected is not null, new Vector2(86f, 0f)))
        {
            _viewModel.DeleteProfileCommand.Execute(null);
        }
    }

    private void RenderBehaviorPage()
    {
        if (!ImGui.BeginChild("behavior-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        RenderResponsiveColumns(
            "behavior-layout",
            900f,
            RenderBehaviorPrimaryColumn,
            RenderBehaviorSecondaryColumn);
        ImGui.EndChild();
    }

    private void RenderHotkeysPage()
    {
        if (!ImGui.BeginChild("hotkeys-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        RenderResponsiveColumns(
            "hotkeys-layout",
            900f,
            RenderHotkeysPrimaryColumn,
            RenderHotkeysSecondaryColumn);

        ImGui.EndChild();
    }

    private void RenderLogsPage()
    {
        if (!ImGui.BeginChild("logs-tab-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        RenderResponsiveColumns(
            "logs-layout",
            960f,
            RenderLogsPrimaryColumn,
            RenderLogsSecondaryColumn,
            0.34f,
            0.66f);
        ImGui.EndChild();
    }

    private void RenderDiagnosticsPage()
    {
        if (!ImGui.BeginChild("diagnostics-scroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        RenderResponsiveColumns(
            "diagnostics-layout",
            900f,
            RenderRuntimeDiagnosticsSection,
            RenderEnvironmentDiagnosticsSection);

        ImGui.EndChild();
    }

    private void RenderProfilePrimaryColumn()
    {
        RenderProfileIdentitySection();
        ImGui.Separator();
        RenderProfileSnapshotSection();
    }

    private void RenderProfileSecondaryColumn()
    {
        RenderProfileExecutionSection();
        ImGui.Separator();
        RenderRouteCanvasSection();
    }

    private void RenderBehaviorPrimaryColumn()
    {
        RenderBehaviorProbabilitySection();
        ImGui.Separator();
        RenderBehaviorSmoothingSection();
    }

    private void RenderBehaviorSecondaryColumn()
    {
        RenderBehaviorPauseSection();

        if (ImGui.CollapsingHeader("Behavior Summary", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("behavior-summary", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Action probability", _viewModel.ActionProbability.ToString("0.00"));
            RenderSummaryRow("Strafe preference", _viewModel.StrafePreference.ToString("0.00"));
            RenderSummaryRow("Movement smoothness", _viewModel.MovementSmoothness.ToString("0.00"));
            RenderSummaryRow("Pause frequency", _viewModel.PauseFrequency.ToString("0.00"));
            RenderSummaryRow("Pause range", $"{_viewModel.MinPauseSeconds:0.00}s - {_viewModel.MaxPauseSeconds:0.00}s");
            ImGui.EndTable();
        }
    }

    private void RenderHotkeysPrimaryColumn()
    {
        if (ImGui.CollapsingHeader("Global Hotkeys", ImGuiTreeNodeFlags.DefaultOpen))
        {
            RenderHotkeyEditor();
        }
    }

    private void RenderHotkeysSecondaryColumn()
    {
        if (ImGui.CollapsingHeader("Application", ImGuiTreeNodeFlags.DefaultOpen))
        {
            RenderApplicationEditor();
        }

        if (ImGui.CollapsingHeader("Hotkey Summary", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("hotkeys-page-summary", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Start", _viewModel.StartHotkeyText);
            RenderSummaryRow("Stop", _viewModel.StopHotkeyText);
            RenderSummaryRow("Pause / Resume", _viewModel.PauseHotkeyText);
            ImGui.EndTable();
        }
    }

    private void RenderLogsPrimaryColumn()
    {
        IReadOnlyList<LogEntry> filteredLogs = GetFilteredLogEntries();

        if (ImGui.CollapsingHeader("Log Tools", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##log-search", "Search message text", ref _logSearchText, 128))
            {
                _selectedLogText = null;
                _selectedLogTimestamp = "-";
                _selectedLogSeverity = "-";
                _selectedLogMessage = "Select a log row to inspect it here.";
            }

            if (ImGui.BeginTable("log-severity-filters", 4, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn();
                ImGui.Checkbox("Debug", ref _showDebugLogs);
                ImGui.TableNextColumn();
                ImGui.Checkbox("Info", ref _showInfoLogs);
                ImGui.TableNextColumn();
                ImGui.Checkbox("Warning", ref _showWarningLogs);
                ImGui.TableNextColumn();
                ImGui.Checkbox("Error", ref _showErrorLogs);
                ImGui.EndTable();
            }

            bool autoScroll = _autoScrollLogs;
            if (ImGui.Checkbox("Auto-scroll", ref autoScroll))
            {
                _autoScrollLogs = autoScroll;
            }

            ImGui.BeginDisabled(filteredLogs.Count == 0);
            if (ImGui.Button("Copy visible"))
            {
                Clipboard.SetText(string.Join(Environment.NewLine, filteredLogs.Select(FormatLogEntry)));
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy selected"))
            {
                Clipboard.SetText(_selectedLogText ?? string.Empty);
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear logs"))
            {
                _viewModel.ClearLogs();
                _selectedLogText = null;
                _selectedLogTimestamp = "-";
                _selectedLogSeverity = "-";
                _selectedLogMessage = "Log history was cleared.";
            }

            ImGui.EndDisabled();
        }

        IReadOnlyList<LogEntry> allLogs = _viewModel.GetLogEntries();
        if (ImGui.CollapsingHeader("Snapshot", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("log-status", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Visible entries", filteredLogs.Count.ToString());
            RenderSummaryRow("Buffered entries", allLogs.Count.ToString());
            RenderSummaryRow("Persist to disk", _viewModel.PersistLogsToDisk ? "Yes" : "No");
            RenderSummaryRow("Max entries", _viewModel.MaxLogEntries.ToString());
            RenderSummaryRow("Log file", _viewModel.LogFilePath);
            ImGui.EndTable();
        }

        if (ImGui.CollapsingHeader("Selected Entry", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("selected-log-entry", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                RenderSummaryRow("Timestamp", _selectedLogTimestamp);
                RenderSummaryRow("Severity", _selectedLogSeverity);
                ImGui.EndTable();
            }

            Vector2 detailSize = new(ImGui.GetContentRegionAvail().X, MathF.Max(120f, ImGui.GetContentRegionAvail().Y - 4f));
            if (ImGui.BeginChild("selected-log-message", detailSize, ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
            {
                ImGui.TextWrapped(_selectedLogMessage);
            }

            ImGui.EndChild();
        }
    }

    private void RenderLogsSecondaryColumn()
    {
        IReadOnlyList<LogEntry> filteredLogs = GetFilteredLogEntries();
        if (ImGui.CollapsingHeader("Log Stream", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Vector2 size = new(ImGui.GetContentRegionAvail().X, MathF.Max(360f, ImGui.GetContentRegionAvail().Y - 4f));
            if (ImGui.BeginChild("logs-scroll", size, ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
            {
                if (ImGui.BeginTable(
                        "logs-table",
                        3,
                        ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.Borders |
                        ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.Resizable |
                        ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 88f);
                    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 72f);
                    ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (LogEntry entry in filteredLogs)
                    {
                        string formatted = FormatLogEntry(entry);
                        bool isSelected = string.Equals(_selectedLogText, formatted, StringComparison.Ordinal);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        if (ImGui.Selectable(
                                entry.Timestamp.ToString("HH:mm:ss"),
                                isSelected,
                                ImGuiSelectableFlags.SpanAllColumns))
                        {
                            _selectedLogText = formatted;
                            _selectedLogTimestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                            _selectedLogSeverity = entry.Severity.ToString();
                            _selectedLogMessage = entry.Message;
                        }

                        ImGui.TableSetColumnIndex(1);
                        Vector4 severityColor = GetSeverityColor(entry.Severity);
                        ImGui.PushStyleColor(ImGuiCol.Text, severityColor);
                        ImGui.TextUnformatted(entry.Severity.ToString());
                        ImGui.PopStyleColor();

                        ImGui.TableSetColumnIndex(2);
                        ImGui.TextWrapped(entry.Message);
                    }

                    if (_autoScrollLogs && filteredLogs.Count > 0)
                    {
                        ImGui.SetScrollHereY(1f);
                    }

                    ImGui.EndTable();
                }
                else if (filteredLogs.Count == 0)
                {
                    ImGui.TextDisabled("No log entries match the current filters.");
                }
            }

            ImGui.EndChild();
        }
    }

    private void RenderProfileTab()
    {
        RenderProfileIdentitySection();
        RenderProfileExecutionSection();
        RenderRouteCanvasSection();
        RenderProfileSnapshotSection();
    }

    private void RenderBehaviorTab()
    {
        RenderBehaviorProbabilitySection();
        RenderBehaviorSmoothingSection();
        RenderBehaviorPauseSection();
    }

    private void RenderShortcutsTab()
    {
        if (ImGui.CollapsingHeader("Global Hotkeys", ImGuiTreeNodeFlags.DefaultOpen))
        {
            RenderHotkeyEditor();
        }

        if (ImGui.CollapsingHeader("Application", ImGuiTreeNodeFlags.DefaultOpen))
        {
            RenderApplicationEditor();
        }
    }

    private void RenderRuntimeDiagnosticsSection()
    {
        if (ImGui.CollapsingHeader("Runtime State", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("runtime-state", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Status", _viewModel.StatusText);
            RenderSummaryRow("Detail", _viewModel.StatusDetail);
            RenderSummaryRow("Pause action", _viewModel.PauseResumeText);
            RenderSummaryRow("Can start", _viewModel.CanStart ? "Yes" : "No");
            RenderSummaryRow("Can stop", _viewModel.CanStop ? "Yes" : "No");
            RenderSummaryRow("Can pause", _viewModel.CanPause ? "Yes" : "No");
            RenderSummaryRow("Profile", _viewModel.SelectedProfile?.Name ?? "<none>");
            RenderSummaryRow("Mode", FormatEnumLabel(_viewModel.SelectedMode));
            RenderSummaryRow("Input", FormatEnumLabel(_viewModel.SelectedInputStrategy));
            ImGui.EndTable();
        }

        if (ImGui.CollapsingHeader("Target Probe", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (RenderCommandButton(
                    _viewModel.IsWindowSearchRunning ? "Searching..." : "Refresh target scan",
                    !_viewModel.IsWindowSearchRunning,
                    new Vector2(180f, 0f)))
            {
                _viewModel.RefreshWindowProbeCommand.Execute(null);
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy diagnostics snapshot"))
            {
                Clipboard.SetText(BuildDiagnosticsSnapshot());
            }

            ImGui.TextWrapped(_viewModel.WindowSearchSummary);

            if (ImGui.BeginTable("runtime-target", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                RenderSummaryRow("Expected process", _viewModel.ValorantProcessNameHintText);
                RenderSummaryRow("Detected title", _viewModel.DetectedWindowTitle);
                RenderSummaryRow("Detected process", _viewModel.DetectedProcessName);
                RenderSummaryRow("PID", _viewModel.DetectedProcessId);
                RenderSummaryRow("Window handle", _viewModel.DetectedWindowHandle);
                ImGui.EndTable();
            }
        }
    }

    private void RenderEnvironmentDiagnosticsSection()
    {
        if (ImGui.CollapsingHeader("Execution Profile", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("diagnostics-profile", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Pattern", FormatEnumLabel(_viewModel.PatternType));
            if (_viewModel.ShowJumpingSettings)
            {
                RenderSummaryRow("Jump delay", $"{_viewModel.JumpDelaySeconds:0.00}s");
            }
            else
            {
                RenderSummaryRow("Movement path", _viewModel.MovementPath);
                RenderSummaryRow("Movement intensity", _viewModel.MovementIntensity.ToString("0.00"));
                RenderSummaryRow("Direction change", _viewModel.DirectionChangeFrequency.ToString("0.00"));
                RenderSummaryRow("Action probability", _viewModel.ActionProbability.ToString("0.00"));
                RenderSummaryRow("Pause frequency", _viewModel.PauseFrequency.ToString("0.00"));
                RenderSummaryRow("Pause range", $"{_viewModel.MinPauseSeconds:0.00}s - {_viewModel.MaxPauseSeconds:0.00}s");
            }

            if (_viewModel.IsPathFollowMode)
            {
                RenderSummaryRow("Route points", _viewModel.RouteCanvasPoints.Count.ToString());
            }

            ImGui.EndTable();
        }

        if (ImGui.CollapsingHeader("Application Files", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("diagnostics-paths", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
            {
                RenderSummaryRow("App data", _viewModel.DataDirectoryPath);
                RenderSummaryRow("Config", _viewModel.ConfigFilePath);
                RenderSummaryRow("Log file", _viewModel.LogFilePath);
                RenderSummaryRow("Executable", _viewModel.ExecutablePath);
                ImGui.EndTable();
            }

            if (ImGui.Button("Open app data"))
            {
                OpenPathInExplorer(_viewModel.DataDirectoryPath);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open config"))
            {
                OpenPathInExplorer(_viewModel.ConfigFilePath, selectFile: true);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open log file"))
            {
                OpenPathInExplorer(_viewModel.LogFilePath, selectFile: true);
            }
        }

        if (ImGui.CollapsingHeader("Runtime Settings", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("diagnostics-settings", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Close action", _viewModel.MinimizeToTrayOnClose ? "Hide to tray" : "Exit application");
            RenderSummaryRow("Launch on startup", _viewModel.LaunchOnStartup ? "Enabled" : "Disabled");
            RenderSummaryRow("Persist logs", _viewModel.PersistLogsToDisk ? "Enabled" : "Disabled");
            RenderSummaryRow("Max buffered logs", _viewModel.MaxLogEntries.ToString());
            ImGui.EndTable();
        }
    }

    private void RenderModeCombo()
    {
        int currentIndex = Array.IndexOf(_viewModel.ModeOptions.ToArray(), _viewModel.SelectedMode);
        string[] labels = _viewModel.ModeOptions.Select(FormatEnumLabel).ToArray();
        float reservedLabelWidth = MathF.Max(110f, ImGui.CalcTextSize("Mode").X + 72f);
        ImGui.SetNextItemWidth(MathF.Max(220f, ImGui.GetContentRegionAvail().X - reservedLabelWidth));
        if (ImGui.Combo("Mode", ref currentIndex, labels, labels.Length))
        {
            _viewModel.SelectedMode = _viewModel.ModeOptions[currentIndex];
        }
    }

    private void RenderInputStrategyCombo()
    {
        int currentIndex = Array.IndexOf(_viewModel.InputStrategyOptions.ToArray(), _viewModel.SelectedInputStrategy);
        string[] labels = _viewModel.InputStrategyOptions.Select(FormatEnumLabel).ToArray();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("Input strategy", ref currentIndex, labels, labels.Length))
        {
            _viewModel.SelectedInputStrategy = _viewModel.InputStrategyOptions[currentIndex];
        }
    }

    private void RenderHotkeyEditor()
    {
        string startHotkey = _viewModel.StartHotkeyText;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Start hotkey", ref startHotkey, 64))
        {
            _viewModel.StartHotkeyText = startHotkey;
        }

        string stopHotkey = _viewModel.StopHotkeyText;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Stop hotkey", ref stopHotkey, 64))
        {
            _viewModel.StopHotkeyText = stopHotkey;
        }

        string pauseHotkey = _viewModel.PauseHotkeyText;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Pause hotkey", ref pauseHotkey, 64))
        {
            _viewModel.PauseHotkeyText = pauseHotkey;
        }

        if (ImGui.Button("Apply hotkeys"))
        {
            _viewModel.ApplyHotkeysCommand.Execute(null);
        }
    }

    private void RenderApplicationEditor()
    {
        bool minimizeToTray = _viewModel.MinimizeToTrayOnClose;
        if (ImGui.Checkbox("Minimize to tray on close", ref minimizeToTray))
        {
            _viewModel.MinimizeToTrayOnClose = minimizeToTray;
        }

        bool launchOnStartup = _viewModel.LaunchOnStartup;
        if (ImGui.Checkbox("Launch on Windows startup", ref launchOnStartup))
        {
            _viewModel.LaunchOnStartup = launchOnStartup;
        }

        bool persistLogs = _viewModel.PersistLogsToDisk;
        if (ImGui.Checkbox("Persist logs to disk", ref persistLogs))
        {
            _viewModel.PersistLogsToDisk = persistLogs;
        }

        int maxLogEntries = _viewModel.MaxLogEntries;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputInt("Max in-memory log entries", ref maxLogEntries))
        {
            _viewModel.MaxLogEntries = Math.Clamp(maxLogEntries, 100, 5000);
        }
    }

    private void RenderProfileIdentitySection()
    {
        if (ImGui.CollapsingHeader("Identity", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string profileName = _viewModel.ProfileName;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("Profile name", ref profileName, 128))
            {
                _viewModel.ProfileName = profileName;
            }

            RenderModeCombo();
            RenderInputStrategyCombo();
        }
    }

    private void RenderProfileExecutionSection()
    {
        if (ImGui.CollapsingHeader("Execution", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_viewModel.ShowJumpingSettings)
            {
                float jumpDelay = (float)_viewModel.JumpDelaySeconds;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.SliderFloat("Jump delay (sec)", ref jumpDelay, 0.1f, 60f, "%.2f"))
                {
                    _viewModel.JumpDelaySeconds = jumpDelay;
                }

                ImGui.TextDisabled("Jumping mode keeps the loop intentionally minimal.");
                return;
            }

            float keyDelay = (float)_viewModel.KeyPressDelaySeconds;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Key press delay (sec)", ref keyDelay, 0.1f, 5f, "%.2f"))
            {
                _viewModel.KeyPressDelaySeconds = keyDelay;
            }

            if (_viewModel.IsPathFollowMode)
            {
                ImGui.TextDisabled("Pattern is fixed to Path Follow for this mode.");
            }
            else
            {
                int patternIndex = Array.IndexOf(_viewModel.PatternOptions.ToArray(), _viewModel.PatternType);
                string[] patternLabels = _viewModel.PatternOptions.Select(FormatEnumLabel).ToArray();
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.Combo("Pattern", ref patternIndex, patternLabels, patternLabels.Length))
                {
                    _viewModel.PatternType = _viewModel.PatternOptions[patternIndex];
                }

                string movementPath = _viewModel.MovementPath;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText("Movement path", ref movementPath, 16))
                {
                    _viewModel.MovementPath = movementPath;
                }
            }

            float intensity = (float)_viewModel.MovementIntensity;
            if (ImGui.SliderFloat("Movement intensity", ref intensity, 0.1f, 1f, "%.2f"))
            {
                _viewModel.MovementIntensity = intensity;
            }

            float frequency = (float)_viewModel.DirectionChangeFrequency;
            if (ImGui.SliderFloat("Direction change frequency", ref frequency, 0.1f, 1f, "%.2f"))
            {
                _viewModel.DirectionChangeFrequency = frequency;
            }
        }
    }

    private void RenderRouteCanvasSection()
    {
        if (!ImGui.CollapsingHeader("Path Follow Canvas", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _isRouteCanvasDrawing = false;
            _isRouteCanvasPanning = false;
            return;
        }

        SyncRouteCanvasDraft();
        ImGui.TextDisabled("Hold left mouse to draw. Hold middle mouse to pan. Use Ctrl + Wheel to zoom.");

        if (ImGui.Button("Use Path Follow"))
        {
            _viewModel.SelectedMode = AntiAfkMode.PathFollow;
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            _viewModel.ResetRouteCanvas();
            SyncRouteCanvasDraft(force: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _routeCanvasDraft.Clear();
            _viewModel.ResetRouteCanvas();
            SyncRouteCanvasDraft(force: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset View"))
        {
            ResetRouteCanvasView();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_viewModel.IsPathFollowMode ? "Path Follow active" : "Path Follow inactive");
        ImGui.TextDisabled($"Zoom: {_routeCanvasZoom * 100f:0}% (Ctrl + Wheel)");

        Vector2 available = ImGui.GetContentRegionAvail();
        float canvasWidth = MathF.Max(260f, available.X);
        float canvasHeight = Math.Clamp(canvasWidth * 0.48f, 180f, 260f);
        Vector2 canvasSize = new(canvasWidth, canvasHeight);
        Vector2 canvasPosition = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton("route-canvas", canvasSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        uint backgroundColor = ImGui.GetColorU32(new Vector4(0.08f, 0.09f, 0.11f, 1f));
        uint borderColor = ImGui.GetColorU32(new Vector4(0.33f, 0.44f, 0.62f, 1f));
        uint gridColor = ImGui.GetColorU32(new Vector4(0.22f, 0.26f, 0.33f, 1f));
        uint gridMajorColor = ImGui.GetColorU32(new Vector4(0.30f, 0.35f, 0.43f, 1f));
        uint lineColor = ImGui.GetColorU32(new Vector4(0.33f, 0.57f, 0.88f, 1f));
        uint pointColor = ImGui.GetColorU32(new Vector4(0.85f, 0.92f, 1.00f, 1f));
        uint activePointColor = ImGui.GetColorU32(new Vector4(0.98f, 0.75f, 0.29f, 1f));

        drawList.AddRectFilled(canvasPosition, canvasPosition + canvasSize, backgroundColor, 6f);
        drawList.AddRect(canvasPosition, canvasPosition + canvasSize, borderColor, 6f, ImDrawFlags.None, 1.5f);
        drawList.PushClipRect(canvasPosition, canvasPosition + canvasSize, true);

        DrawRouteCanvasGrid(drawList, canvasPosition, canvasSize, gridColor, gridMajorColor);

        IReadOnlyList<RouteCanvasPoint> previewRoute = BuildSmoothedRoutePreview(_routeCanvasDraft);
        for (int index = 0; index < previewRoute.Count - 1; index++)
        {
            Vector2 start = CanvasPointToScreen(previewRoute[index], canvasPosition, canvasSize);
            Vector2 end = CanvasPointToScreen(previewRoute[index + 1], canvasPosition, canvasSize);
            drawList.AddLine(start, end, lineColor, 2f);
        }

        for (int index = 0; index < _routeCanvasDraft.Count; index++)
        {
            Vector2 point = CanvasPointToScreen(_routeCanvasDraft[index], canvasPosition, canvasSize);
            drawList.AddCircleFilled(point, 4.5f, _isRouteCanvasDrawing ? activePointColor : pointColor);
        }

        drawList.PopClipRect();
        HandleRouteCanvasInput(hovered, active, canvasPosition, canvasSize);
    }

    private void RenderProfileSnapshotSection()
    {
        if (ImGui.CollapsingHeader("Current Snapshot", ImGuiTreeNodeFlags.DefaultOpen) &&
            ImGui.BeginTable("profile-snapshot", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            RenderSummaryRow("Action probability", _viewModel.ActionProbability.ToString("0.00"));
            RenderSummaryRow("Strafe preference", _viewModel.StrafePreference.ToString("0.00"));
            RenderSummaryRow("Movement smoothness", _viewModel.MovementSmoothness.ToString("0.00"));
            RenderSummaryRow("Pause frequency", _viewModel.PauseFrequency.ToString("0.00"));
            RenderSummaryRow("Pause range", $"{_viewModel.MinPauseSeconds:0.00}s - {_viewModel.MaxPauseSeconds:0.00}s");
            ImGui.EndTable();
        }
    }

    private void RenderBehaviorProbabilitySection()
    {
        if (ImGui.CollapsingHeader("Probabilities", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float actionProbability = (float)_viewModel.ActionProbability;
            if (ImGui.SliderFloat("Action probability", ref actionProbability, 0f, 1f, "%.2f"))
            {
                _viewModel.ActionProbability = actionProbability;
            }

            float strafePreference = (float)_viewModel.StrafePreference;
            if (ImGui.SliderFloat("Strafe preference", ref strafePreference, 0f, 1f, "%.2f"))
            {
                _viewModel.StrafePreference = strafePreference;
            }

            float pauseFrequency = (float)_viewModel.PauseFrequency;
            if (ImGui.SliderFloat("Pause frequency", ref pauseFrequency, 0f, 1f, "%.2f"))
            {
                _viewModel.PauseFrequency = pauseFrequency;
            }
        }
    }

    private void RenderBehaviorSmoothingSection()
    {
        if (ImGui.CollapsingHeader("Smoothing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float smoothness = (float)_viewModel.MovementSmoothness;
            if (ImGui.SliderFloat("Movement smoothness", ref smoothness, 0.1f, 1f, "%.2f"))
            {
                _viewModel.MovementSmoothness = smoothness;
            }
        }
    }

    private void RenderBehaviorPauseSection()
    {
        if (ImGui.CollapsingHeader("Pause Envelope", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float minPause = (float)_viewModel.MinPauseSeconds;
            if (ImGui.InputFloat("Min pause (sec)", ref minPause, 0.1f, 1f, "%.2f"))
            {
                _viewModel.MinPauseSeconds = Math.Max(0.1f, minPause);
            }

            float maxPause = (float)_viewModel.MaxPauseSeconds;
            if (ImGui.InputFloat("Max pause (sec)", ref maxPause, 0.1f, 1f, "%.2f"))
            {
                _viewModel.MaxPauseSeconds = Math.Max((float)_viewModel.MinPauseSeconds, maxPause);
            }
        }
    }

    private IReadOnlyList<LogEntry> GetFilteredLogEntries()
    {
        IReadOnlyList<LogEntry> entries = _viewModel.GetLogEntries();
        if (entries.Count == 0)
        {
            return entries;
        }

        return entries
            .Where(entry => IsSeverityVisible(entry.Severity))
            .Where(entry => string.IsNullOrWhiteSpace(_logSearchText) ||
                entry.Message.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.Severity.ToString().Contains(_logSearchText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private bool IsSeverityVisible(LogSeverity severity) => severity switch
    {
        LogSeverity.Debug => _showDebugLogs,
        LogSeverity.Info => _showInfoLogs,
        LogSeverity.Warning => _showWarningLogs,
        LogSeverity.Error => _showErrorLogs,
        _ => true,
    };

    private static string FormatLogEntry(LogEntry entry) =>
        $"[{entry.Timestamp:HH:mm:ss}] [{entry.Severity}] {entry.Message}";

    private static Vector4 GetSeverityColor(LogSeverity severity) => severity switch
    {
        LogSeverity.Debug => new Vector4(0.62f, 0.65f, 0.72f, 1f),
        LogSeverity.Info => new Vector4(0.52f, 0.76f, 0.96f, 1f),
        LogSeverity.Warning => new Vector4(0.98f, 0.78f, 0.34f, 1f),
        LogSeverity.Error => new Vector4(0.94f, 0.44f, 0.44f, 1f),
        _ => Vector4.One,
    };

    private string BuildDiagnosticsSnapshot()
    {
        string[] lines =
        [
            $"Status: {_viewModel.StatusText}",
            $"Status detail: {_viewModel.StatusDetail}",
            $"Profile: {_viewModel.SelectedProfile?.Name ?? "<none>"}",
            $"Mode: {FormatEnumLabel(_viewModel.SelectedMode)}",
            $"Input: {FormatEnumLabel(_viewModel.SelectedInputStrategy)}",
            $"Target summary: {_viewModel.WindowSearchSummary}",
            $"Expected process: {_viewModel.ValorantProcessNameHintText}",
            $"Detected title: {_viewModel.DetectedWindowTitle}",
            $"Detected process: {_viewModel.DetectedProcessName}",
            $"PID: {_viewModel.DetectedProcessId}",
            $"Window handle: {_viewModel.DetectedWindowHandle}",
            $"App data: {_viewModel.DataDirectoryPath}",
            $"Config: {_viewModel.ConfigFilePath}",
            $"Log file: {_viewModel.LogFilePath}",
        ];

        return string.Join(Environment.NewLine, lines);
    }

    private static void OpenPathInExplorer(string path, bool selectFile = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalizedPath = path;
        string targetPath = selectFile
            ? (System.IO.File.Exists(normalizedPath) ? normalizedPath : System.IO.Path.GetDirectoryName(normalizedPath) ?? normalizedPath)
            : normalizedPath;

        if (!System.IO.File.Exists(targetPath) && !System.IO.Directory.Exists(targetPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = selectFile && System.IO.File.Exists(normalizedPath)
                ? $"/select,\"{normalizedPath}\""
                : $"\"{targetPath}\"",
            UseShellExecute = true,
        });
    }

    private void RenderResponsiveColumns(
        string tableId,
        float breakpoint,
        Action left,
        Action right,
        float leftWeight = 0.5f,
        float rightWeight = 0.5f)
    {
        bool wide = ImGui.GetContentRegionAvail().X >= breakpoint;
        if (wide && ImGui.BeginTable(tableId, 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn($"{tableId}-left", ImGuiTableColumnFlags.WidthStretch, leftWeight);
            ImGui.TableSetupColumn($"{tableId}-right", ImGuiTableColumnFlags.WidthStretch, rightWeight);
            ImGui.TableNextColumn();
            left();
            ImGui.TableNextColumn();
            right();
            ImGui.EndTable();
            return;
        }

        left();
        ImGui.Separator();
        right();
    }

    private static void RenderSummaryRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }

    private static bool RenderCommandButton(string label, bool enabled, Vector2 size)
    {
        ImGui.BeginDisabled(!enabled);
        bool clicked = ImGui.Button(label, size);
        ImGui.EndDisabled();
        return clicked;
    }

    private static void RenderBullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted(text);
    }

    private static string FormatEnumLabel<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString()
            .Replace("Wasd", "WASD", StringComparison.Ordinal)
            .Replace("PathFollow", "Path Follow", StringComparison.Ordinal)
            .Replace("RouteCanvas", "Path Follow", StringComparison.Ordinal)
            .Replace("ForegroundSendInput", "Foreground SendInput", StringComparison.Ordinal)
            .Replace("WindowMessage", "Window Message", StringComparison.Ordinal)
            .Replace("ForwardBack", "Forward/Back", StringComparison.Ordinal)
            .Replace("PauseResume", "Pause / Resume", StringComparison.Ordinal);

    private void SyncRouteCanvasDraft(bool force = false)
    {
        string? profileId = _viewModel.SelectedProfile?.Id;
        bool profileChanged = _routeCanvasDraftProfileId != profileId;
        if (!force && _routeCanvasDraftProfileId == profileId)
        {
            return;
        }

        _routeCanvasDraft.Clear();
        _routeCanvasDraft.AddRange(_viewModel.RouteCanvasPoints);
        _routeCanvasDraftProfileId = profileId;
        _isRouteCanvasDrawing = false;
        _isRouteCanvasPanning = false;
        if (profileChanged)
        {
            ResetRouteCanvasView();
        }
    }

    private void HandleRouteCanvasInput(bool hovered, bool active, Vector2 canvasPosition, Vector2 canvasSize)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        Vector2 mouse = io.MousePos;
        HandleRouteCanvasZoom(hovered, io, mouse, canvasPosition, canvasSize);
        HandleRouteCanvasPan(hovered, io, canvasSize);

        if (!_isRouteCanvasPanning && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _routeCanvasDraft.Clear();
            _routeCanvasDraft.Add(ScreenToCanvasPoint(mouse, canvasPosition, canvasSize));
            _isRouteCanvasDrawing = true;
        }

        if (_isRouteCanvasDrawing && !_isRouteCanvasPanning && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            TryAppendRouteDrawPoint(mouse, canvasPosition, canvasSize);
        }

        if (_isRouteCanvasDrawing && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_routeCanvasDraft.Count >= 2)
            {
                CommitRouteCanvasDraft();
            }
            else
            {
                SyncRouteCanvasDraft(force: true);
            }

            _isRouteCanvasDrawing = false;
        }

        if (!active && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _isRouteCanvasDrawing = false;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            _isRouteCanvasPanning = false;
        }
    }

    private void CommitRouteCanvasDraft()
    {
        _viewModel.SetRouteCanvasPoints(_routeCanvasDraft.ToList());
        SyncRouteCanvasDraft(force: true);
    }

    private void TryAppendRouteDrawPoint(Vector2 mouse, Vector2 canvasPosition, Vector2 canvasSize)
    {
        RouteCanvasPoint next = ScreenToCanvasPoint(mouse, canvasPosition, canvasSize);
        if (_routeCanvasDraft.Count == 0)
        {
            _routeCanvasDraft.Add(next);
            return;
        }

        RouteCanvasPoint last = _routeCanvasDraft[^1];
        float dx = (float)(next.X - last.X);
        float dy = (float)(next.Y - last.Y);
        if (MathF.Sqrt((dx * dx) + (dy * dy)) >= 0.01f)
        {
            _routeCanvasDraft.Add(next);
        }
    }

    private Vector2 CanvasPointToScreen(RouteCanvasPoint point, Vector2 canvasPosition, Vector2 canvasSize) =>
        new(
            canvasPosition.X + (((float)point.X - _routeCanvasViewCenter.X) * _routeCanvasZoom + 0.5f) * canvasSize.X,
            canvasPosition.Y + (((float)point.Y - _routeCanvasViewCenter.Y) * _routeCanvasZoom + 0.5f) * canvasSize.Y);

    private float CanvasPointToScreenX(float normalizedX, Vector2 canvasPosition, Vector2 canvasSize) =>
        canvasPosition.X + (((normalizedX - _routeCanvasViewCenter.X) * _routeCanvasZoom) + 0.5f) * canvasSize.X;

    private float CanvasPointToScreenY(float normalizedY, Vector2 canvasPosition, Vector2 canvasSize) =>
        canvasPosition.Y + (((normalizedY - _routeCanvasViewCenter.Y) * _routeCanvasZoom) + 0.5f) * canvasSize.Y;

    private RouteCanvasPoint ScreenToCanvasPoint(Vector2 mouse, Vector2 canvasPosition, Vector2 canvasSize)
    {
        float normalizedX = ((mouse.X - canvasPosition.X) / canvasSize.X) - 0.5f;
        float normalizedY = ((mouse.Y - canvasPosition.Y) / canvasSize.Y) - 0.5f;

        return new RouteCanvasPoint
        {
            X = Math.Clamp(_routeCanvasViewCenter.X + (normalizedX / _routeCanvasZoom), RouteCanvasWorldMin, RouteCanvasWorldMax),
            Y = Math.Clamp(_routeCanvasViewCenter.Y + (normalizedY / _routeCanvasZoom), RouteCanvasWorldMin, RouteCanvasWorldMax),
        };
    }

    private void HandleRouteCanvasZoom(bool hovered, ImGuiIOPtr io, Vector2 mouse, Vector2 canvasPosition, Vector2 canvasSize)
    {
        if (!hovered || !io.KeyCtrl || Math.Abs(io.MouseWheel) <= float.Epsilon)
        {
            return;
        }

        float previousZoom = _routeCanvasZoom;
        RouteCanvasPoint anchorPoint = ScreenToCanvasPoint(mouse, canvasPosition, canvasSize);
        float zoomFactor = MathF.Pow(1.18f, io.MouseWheel);
        _routeCanvasZoom = Math.Clamp(_routeCanvasZoom * zoomFactor, 0.5f, 6f);
        if (Math.Abs(_routeCanvasZoom - previousZoom) <= float.Epsilon)
        {
            return;
        }

        Vector2 cursorUv = new(
            Math.Clamp((mouse.X - canvasPosition.X) / canvasSize.X, 0f, 1f),
            Math.Clamp((mouse.Y - canvasPosition.Y) / canvasSize.Y, 0f, 1f));

        _routeCanvasViewCenter = new Vector2(
            (float)anchorPoint.X - ((cursorUv.X - 0.5f) / _routeCanvasZoom),
            (float)anchorPoint.Y - ((cursorUv.Y - 0.5f) / _routeCanvasZoom));
        ClampRouteCanvasViewCenter();
    }

    private void HandleRouteCanvasPan(bool hovered, ImGuiIOPtr io, Vector2 canvasSize)
    {
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            _isRouteCanvasPanning = true;
        }

        if (!_isRouteCanvasPanning || !ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            return;
        }

        _routeCanvasViewCenter -= new Vector2(
            io.MouseDelta.X / (canvasSize.X * _routeCanvasZoom),
            io.MouseDelta.Y / (canvasSize.Y * _routeCanvasZoom));
        ClampRouteCanvasViewCenter();
    }

    private void ResetRouteCanvasView()
    {
        _routeCanvasZoom = 1f;
        _routeCanvasViewCenter = DefaultRouteCanvasViewCenter;
    }

    private void ClampRouteCanvasViewCenter()
    {
        float halfVisibleWidth = 0.5f / _routeCanvasZoom;
        float halfVisibleHeight = 0.5f / _routeCanvasZoom;
        float worldCenter = (RouteCanvasWorldMin + RouteCanvasWorldMax) * 0.5f;
        float worldWidth = RouteCanvasWorldMax - RouteCanvasWorldMin;
        float worldHeight = RouteCanvasWorldMax - RouteCanvasWorldMin;

        float clampedCenterX = halfVisibleWidth * 2f >= worldWidth
            ? worldCenter
            : Math.Clamp(_routeCanvasViewCenter.X, RouteCanvasWorldMin + halfVisibleWidth, RouteCanvasWorldMax - halfVisibleWidth);

        float clampedCenterY = halfVisibleHeight * 2f >= worldHeight
            ? worldCenter
            : Math.Clamp(_routeCanvasViewCenter.Y, RouteCanvasWorldMin + halfVisibleHeight, RouteCanvasWorldMax - halfVisibleHeight);

        _routeCanvasViewCenter = new Vector2(clampedCenterX, clampedCenterY);
    }

    private void DrawRouteCanvasGrid(ImDrawListPtr drawList, Vector2 canvasPosition, Vector2 canvasSize, uint minorColor, uint majorColor)
    {
        (float minX, float maxX, float minY, float maxY) = GetRouteCanvasVisibleBounds();
        const float minorStep = 0.1f;
        const float majorStep = 0.5f;

        DrawGridLines(axis: 'x', minorStep, minorColor, 1f);
        DrawGridLines(axis: 'y', minorStep, minorColor, 1f);
        DrawGridLines(axis: 'x', majorStep, majorColor, 1.35f);
        DrawGridLines(axis: 'y', majorStep, majorColor, 1.35f);

        void DrawGridLines(char axis, float step, uint color, float thickness)
        {
            float min = axis == 'x' ? minX : minY;
            float max = axis == 'x' ? maxX : maxY;
            float start = MathF.Floor(min / step) * step;

            for (float value = start; value <= max + step; value += step)
            {
                if (axis == 'x')
                {
                    float x = CanvasPointToScreenX(value, canvasPosition, canvasSize);
                    drawList.AddLine(new Vector2(x, canvasPosition.Y), new Vector2(x, canvasPosition.Y + canvasSize.Y), color, thickness);
                }
                else
                {
                    float y = CanvasPointToScreenY(value, canvasPosition, canvasSize);
                    drawList.AddLine(new Vector2(canvasPosition.X, y), new Vector2(canvasPosition.X + canvasSize.X, y), color, thickness);
                }
            }
        }
    }

    private (float MinX, float MaxX, float MinY, float MaxY) GetRouteCanvasVisibleBounds()
    {
        float halfVisibleWidth = 0.5f / _routeCanvasZoom;
        float halfVisibleHeight = 0.5f / _routeCanvasZoom;
        return (
            _routeCanvasViewCenter.X - halfVisibleWidth,
            _routeCanvasViewCenter.X + halfVisibleWidth,
            _routeCanvasViewCenter.Y - halfVisibleHeight,
            _routeCanvasViewCenter.Y + halfVisibleHeight);
    }

    private static IReadOnlyList<RouteCanvasPoint> BuildSmoothedRoutePreview(IReadOnlyList<RouteCanvasPoint> points)
    {
        if (points.Count < 2)
        {
            return points;
        }

        List<RouteCanvasPoint> smoothed = points.ToList();
        for (int iteration = 0; iteration < 2; iteration++)
        {
            if (smoothed.Count < 3)
            {
                break;
            }

            List<RouteCanvasPoint> next = [smoothed[0]];
            for (int index = 0; index < smoothed.Count - 1; index++)
            {
                RouteCanvasPoint start = smoothed[index];
                RouteCanvasPoint end = smoothed[index + 1];
                next.Add(Interpolate(start, end, 0.25));
                next.Add(Interpolate(start, end, 0.75));
            }

            next.Add(smoothed[^1]);
            smoothed = next;
        }

        return smoothed;
    }

    private static RouteCanvasPoint Interpolate(RouteCanvasPoint start, RouteCanvasPoint end, double t) =>
        new()
        {
            X = start.X + ((end.X - start.X) * t),
            Y = start.Y + ((end.Y - start.Y) * t),
        };
}
