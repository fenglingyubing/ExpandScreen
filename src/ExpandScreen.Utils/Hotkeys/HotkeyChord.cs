using System.Globalization;

namespace ExpandScreen.Utils.Hotkeys
{
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 1 << 0,
        Control = 1 << 1,
        Shift = 1 << 2,
        Windows = 1 << 3
    }

    public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, int VirtualKey)
    {
        public bool IsEmpty => VirtualKey == 0;

        public override string ToString() => HotkeyChordFormatter.Format(this);

        public static bool TryParse(string? text, out HotkeyChord chord) => HotkeyChordParser.TryParse(text, out chord);
    }

    internal static class HotkeyChordFormatter
    {
        public static string Format(HotkeyChord chord)
        {
            if (chord.VirtualKey == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(5);

            if (chord.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (chord.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (chord.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (chord.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");

            parts.Add(FormatKey(chord.VirtualKey));
            return string.Join("+", parts);
        }

        private static string FormatKey(int vk)
        {
            if (vk is >= 0x30 and <= 0x39)
            {
                return ((char)vk).ToString(CultureInfo.InvariantCulture);
            }

            if (vk is >= 0x41 and <= 0x5A)
            {
                return ((char)vk).ToString(CultureInfo.InvariantCulture);
            }

            return vk switch
            {
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x1B => "Escape",
                0x09 => "Tab",
                0x0D => "Enter",
                0x20 => "Space",
                0x08 => "Backspace",
                0x2E => "Delete",
                0x2D => "Insert",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x2C => "PrintScreen",
                0x13 => "Pause",
                0x14 => "CapsLock",
                0x90 => "NumLock",
                0x91 => "ScrollLock",
                _ when vk is >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
                _ => $"VK_{vk:X2}"
            };
        }
    }

    internal static class HotkeyChordParser
    {
        private static readonly Dictionary<string, HotkeyModifiers> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CTRL"] = HotkeyModifiers.Control,
            ["CONTROL"] = HotkeyModifiers.Control,
            ["ALT"] = HotkeyModifiers.Alt,
            ["SHIFT"] = HotkeyModifiers.Shift,
            ["WIN"] = HotkeyModifiers.Windows,
            ["WINDOWS"] = HotkeyModifiers.Windows
        };

        private static readonly Dictionary<string, int> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["SPACE"] = 0x20,
            ["BACKSPACE"] = 0x08,
            ["BKSP"] = 0x08,
            ["DELETE"] = 0x2E,
            ["DEL"] = 0x2E,
            ["INSERT"] = 0x2D,
            ["INS"] = 0x2D,
            ["HOME"] = 0x24,
            ["END"] = 0x23,
            ["PAGEUP"] = 0x21,
            ["PGUP"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["PGDN"] = 0x22,
            ["PRINTSCREEN"] = 0x2C,
            ["PRTSC"] = 0x2C,
            ["PAUSE"] = 0x13,
            ["CAPSLOCK"] = 0x14,
            ["NUMLOCK"] = 0x90,
            ["SCROLLLOCK"] = 0x91
        };

        public static bool TryParse(string? text, out HotkeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string normalized = text.Trim();
            string compact = normalized.Replace(" ", string.Empty);
            if (compact.StartsWith("+", StringComparison.Ordinal)
                || compact.EndsWith("+", StringComparison.Ordinal)
                || compact.Contains("++", StringComparison.Ordinal))
            {
                return false;
            }

            var tokens = normalized.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return true;
            }

            HotkeyModifiers modifiers = HotkeyModifiers.None;
            int? virtualKey = null;

            foreach (var token in tokens)
            {
                if (ModifierAliases.TryGetValue(token, out var mod))
                {
                    modifiers |= mod;
                    continue;
                }

                if (virtualKey != null)
                {
                    return false;
                }

                if (!TryParseKeyToken(token, out int vk))
                {
                    return false;
                }

                virtualKey = vk;
            }

            if (virtualKey == null)
            {
                return false;
            }

            chord = new HotkeyChord(modifiers, virtualKey.Value);
            return true;
        }

        private static bool TryParseKeyToken(string token, out int virtualKey)
        {
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string t = token.Trim();

            if (KeyAliases.TryGetValue(t, out int vkAlias))
            {
                virtualKey = vkAlias;
                return true;
            }

            if (t.Length == 1)
            {
                char c = char.ToUpperInvariant(t[0]);
                if (c is >= 'A' and <= 'Z')
                {
                    virtualKey = c;
                    return true;
                }

                if (c is >= '0' and <= '9')
                {
                    virtualKey = c;
                    return true;
                }
            }

            if (t.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f)
                && f is >= 1 and <= 24)
            {
                virtualKey = 0x6F + f; // F1=0x70
                return true;
            }

            if (t.StartsWith("VK_", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vkHex))
            {
                virtualKey = vkHex;
                return true;
            }

            return false;
        }
    }
}
