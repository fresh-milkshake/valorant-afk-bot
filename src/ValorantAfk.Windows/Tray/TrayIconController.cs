using System.Drawing;
using System.Windows.Forms;
using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Windows.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _statusItem;

    public TrayIconController(string tooltip, Icon icon)
    {
        _statusItem = new ToolStripMenuItem("Status: Stopped") { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem("Pause");
        _pauseResumeItem.Click += (_, _) => PauseResumeRequested?.Invoke();

        ToolStripMenuItem openItem = new("Open");
        openItem.Click += (_, _) => OpenRequested?.Invoke();

        ToolStripMenuItem startItem = new("Start");
        startItem.Click += (_, _) => StartRequested?.Invoke();

        ToolStripMenuItem stopItem = new("Stop");
        stopItem.Click += (_, _) => StopRequested?.Invoke();

        ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        ContextMenuStrip menu = new();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = tooltip,
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? StartRequested;

    public event Action? StopRequested;

    public event Action? PauseResumeRequested;

    public event Action? ExitRequested;

    public void UpdateStatus(string statusText, bool canPause)
    {
        _statusItem.Text = $"Status: {statusText}";
        _pauseResumeItem.Enabled = canPause;
        _pauseResumeItem.Text = statusText.Equals("Paused", StringComparison.OrdinalIgnoreCase) ? "Resume" : "Pause";
    }

    public void ShowNotification(string title, string message, LogSeverity severity)
    {
        _notifyIcon.BalloonTipIcon = severity switch
        {
            LogSeverity.Error => ToolTipIcon.Error,
            LogSeverity.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info,
        };
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
