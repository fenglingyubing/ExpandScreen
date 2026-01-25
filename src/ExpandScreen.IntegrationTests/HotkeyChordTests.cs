using ExpandScreen.Utils.Hotkeys;
using Xunit;

namespace ExpandScreen.IntegrationTests
{
    public sealed class HotkeyChordTests
    {
        [Theory]
        [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48)]
        [InlineData("Shift+F12", HotkeyModifiers.Shift, 0x7B)]
        [InlineData("Ctrl+Alt+Right", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x27)]
        [InlineData("Ctrl+Alt+1", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x31)]
        public void Parse_KnownHotkeys_Succeeds(string text, HotkeyModifiers modifiers, int vk)
        {
            Assert.True(HotkeyChord.TryParse(text, out var chord));
            Assert.Equal(modifiers, chord.Modifiers);
            Assert.Equal(vk, chord.VirtualKey);
            Assert.Equal(text, chord.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_Empty_IsAllowed(string text)
        {
            Assert.True(HotkeyChord.TryParse(text, out var chord));
            Assert.True(chord.IsEmpty);
        }

        [Theory]
        [InlineData("Ctrl+Alt")]
        [InlineData("Ctrl++A")]
        [InlineData("Ctrl+Alt+NopeKey")]
        [InlineData("Ctrl+Alt+A+B")]
        public void Parse_Invalid_ReturnsFalse(string text)
        {
            Assert.False(HotkeyChord.TryParse(text, out _));
        }
    }
}

