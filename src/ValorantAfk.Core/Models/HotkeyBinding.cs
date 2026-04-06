using ValorantAfkBot.Core.Enums;

namespace ValorantAfkBot.Core.Models;

public sealed record class HotkeyBinding
{
    public required HotkeyAction Action { get; init; }

    public required HotkeyModifiers Modifiers { get; init; }

    public required int VirtualKey { get; init; }

    public static IReadOnlyList<HotkeyBinding> CreateDefaults() =>
    [
        new HotkeyBinding
        {
            Action = HotkeyAction.Start,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            VirtualKey = (int)Enums.VirtualKey.F9,
        },
        new HotkeyBinding
        {
            Action = HotkeyAction.Stop,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            VirtualKey = (int)Enums.VirtualKey.F10,
        },
        new HotkeyBinding
        {
            Action = HotkeyAction.PauseResume,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            VirtualKey = (int)Enums.VirtualKey.F11,
        },
    ];
}
