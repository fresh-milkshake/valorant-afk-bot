namespace ValorantAfkBot.Core.Models;

public sealed record class WindowDescriptor(nint Handle, int ProcessId, string Title, string? ProcessName = null);
