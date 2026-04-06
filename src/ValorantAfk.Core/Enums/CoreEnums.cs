namespace ValorantAfkBot.Core.Enums;

public enum AntiAfkMode
{
    Jumping = 0,
    Wasd = 1,
    PathFollow = 2,
}

public enum MovementPattern
{
    Random = 0,
    Circle = 1,
    Strafe = 2,
    ForwardBack = 3,
    Custom = 4,
    RouteCanvas = 5,
}

public enum InputStrategyType
{
    WindowMessage = 0,
    ForegroundSendInput = 1,
}

public enum EngineStatus
{
    Stopped = 0,
    WaitingForGame = 1,
    Running = 2,
    Paused = 3,
    Error = 4,
}

public enum HotkeyAction
{
    Start = 0,
    Stop = 1,
    PauseResume = 2,
}

public enum LogSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public enum VirtualKey : ushort
{
    W = 0x57,
    A = 0x41,
    S = 0x53,
    D = 0x44,
    Space = 0x20,
    Shift = 0x10,
    Control = 0x11,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
}
