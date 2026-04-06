using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Interfaces;

public interface IAppLogger
{
    void Log(LogSeverity severity, string message);
}
