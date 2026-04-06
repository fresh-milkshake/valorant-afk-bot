using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Models;

public sealed class EngineStatusChangedEventArgs : EventArgs
{
    public EngineStatusChangedEventArgs(EngineStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public EngineStatus Status { get; }

    public string Message { get; }
}
