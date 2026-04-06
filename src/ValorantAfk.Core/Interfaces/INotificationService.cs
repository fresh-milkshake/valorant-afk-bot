using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Interfaces;

public interface INotificationService
{
    void ShowNotification(string title, string message, LogSeverity severity = LogSeverity.Info);
}
