using System.Text;

namespace RadioWindows.Core.Models;

public enum HotKeyAction
{
    PlayPause,
    NextStation,
}

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Virtual-key codes used by RegisterHotKey on Windows.</summary>
public enum HotKeyKey
{
    P = 0x50,
    N = 0x4E,
    Space = 0x20,
    Left = 0x25,
    Right = 0x27,
    F8 = 0x77,
    F9 = 0x78,
}

/// <summary>A global shortcut, stored as <c>Ctrl+Alt+P</c>.</summary>
public sealed record HotKeyCombo(HotKeyKey Key, HotKeyModifiers Modifiers)
{
    public static readonly HotKeyCombo DefaultPlayPause = new(HotKeyKey.P, HotKeyModifiers.Control | HotKeyModifiers.Alt);

    public static readonly HotKeyCombo DefaultNext = new(HotKeyKey.Right, HotKeyModifiers.Control | HotKeyModifiers.Alt);

    public static readonly IReadOnlyList<HotKeyModifiers> ModifierChoices =
    [
        HotKeyModifiers.Control | HotKeyModifiers.Alt,
        HotKeyModifiers.Control | HotKeyModifiers.Shift,
        HotKeyModifiers.Alt | HotKeyModifiers.Shift,
        HotKeyModifiers.None,
    ];

    public string Display
    {
        get
        {
            var builder = new StringBuilder();
            if (Modifiers.HasFlag(HotKeyModifiers.Control))
            {
                builder.Append("Ctrl+");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Alt))
            {
                builder.Append("Alt+");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Shift))
            {
                builder.Append("Shift+");
            }

            if (Modifiers.HasFlag(HotKeyModifiers.Win))
            {
                builder.Append("Win+");
            }

            builder.Append(KeyLabel(Key));
            return builder.ToString();
        }
    }

    /// <summary>Why the combination will not work on Windows, or null when it is fine.</summary>
    public string? ConflictReason
    {
        get
        {
            if (Modifiers.HasFlag(HotKeyModifiers.Win))
            {
                return "Loc.HotKey.ConflictWin";
            }

            if (Modifiers == HotKeyModifiers.None)
            {
                return "Loc.HotKey.ConflictBare";
            }

            if (Modifiers == HotKeyModifiers.Control && (Key is HotKeyKey.P or HotKeyKey.N))
            {
                return "Loc.HotKey.ConflictControl";
            }

            if (Modifiers == (HotKeyModifiers.Control | HotKeyModifiers.Alt) && Key == HotKeyKey.Space)
            {
                return null;
            }

            return null;
        }
    }

    public static string KeyLabel(HotKeyKey key) => key switch
    {
        HotKeyKey.P => "P",
        HotKeyKey.N => "N",
        HotKeyKey.Space => "Space",
        HotKeyKey.Left => "←",
        HotKeyKey.Right => "→",
        HotKeyKey.F8 => "F8",
        HotKeyKey.F9 => "F9",
        _ => key.ToString(),
    };

    public string ToStorage()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static HotKeyCombo Parse(string? raw, HotKeyCombo fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var modifiers = HotKeyModifiers.None;
        HotKeyKey? key = null;
        foreach (var part in raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotKeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotKeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotKeyModifiers.Shift;
                    break;
                case "win":
                    modifiers |= HotKeyModifiers.Win;
                    break;
                default:
                    if (Enum.TryParse<HotKeyKey>(part, true, out var parsed))
                    {
                        key = parsed;
                    }

                    break;
            }
        }

        return key is null ? fallback : new HotKeyCombo(key.Value, modifiers);
    }
}
