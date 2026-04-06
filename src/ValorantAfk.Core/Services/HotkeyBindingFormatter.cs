using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public static class HotkeyBindingFormatter
{
    private static readonly Dictionary<string, int> NameToVirtualKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F9"] = (int)VirtualKey.F9,
        ["F10"] = (int)VirtualKey.F10,
        ["F11"] = (int)VirtualKey.F11,
        ["W"] = (int)VirtualKey.W,
        ["A"] = (int)VirtualKey.A,
        ["S"] = (int)VirtualKey.S,
        ["D"] = (int)VirtualKey.D,
        ["SPACE"] = (int)VirtualKey.Space,
    };

    public static string ToDisplayString(HotkeyBinding binding)
    {
        List<string> pieces = [];
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            pieces.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            pieces.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            pieces.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            pieces.Add("Win");
        }

        pieces.Add(ToKeyName(binding.VirtualKey));
        return string.Join('+', pieces);
    }

    public static bool TryParse(string value, HotkeyAction action, out HotkeyBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            modifiers |= parts[index].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };
        }

        if (!NameToVirtualKey.TryGetValue(parts[^1], out int virtualKey))
        {
            return false;
        }

        binding = new HotkeyBinding
        {
            Action = action,
            Modifiers = modifiers,
            VirtualKey = virtualKey,
        };

        return true;
    }

    public static string ToKeyName(int virtualKey) =>
        Enum.IsDefined(typeof(VirtualKey), (ushort)virtualKey)
            ? Enum.GetName(typeof(VirtualKey), (ushort)virtualKey) switch
            {
                nameof(VirtualKey.Space) => "Space",
                string keyName when keyName is not null => keyName.ToUpperInvariant(),
                _ => virtualKey.ToString(),
            }
            : virtualKey.ToString();
}
