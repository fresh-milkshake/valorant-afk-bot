using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Models;

public sealed record class LogEntry(DateTimeOffset Timestamp, LogSeverity Severity, string Message);
