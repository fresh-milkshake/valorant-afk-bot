using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Interfaces;

namespace ValorantAfkBot.Windows.Tray;

public sealed class TrayNotificationService : INotificationService
{
    private readonly TrayIconController _trayIconController;

    public TrayNotificationService(TrayIconController trayIconController)
    {
        _trayIconController = trayIconController;
    }

    public void ShowNotification(string title, string message, LogSeverity severity = LogSeverity.Info) =>
        _trayIconController.ShowNotification(title, message, severity);
}
