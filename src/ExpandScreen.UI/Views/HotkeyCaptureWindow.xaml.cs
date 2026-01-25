using System.Windows;
using System.Windows.Input;
using ExpandScreen.Utils.Hotkeys;

namespace ExpandScreen.UI.Views
{
    public sealed partial class HotkeyCaptureWindow : Window
    {
        public string CapturedText { get; private set; } = string.Empty;

        public HotkeyCaptureWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (e.Key == Key.Back)
            {
                CapturedText = string.Empty;
                DataContext = null;
                DataContext = this;
                e.Handled = true;
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0)
            {
                return;
            }

            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;

            var chord = new HotkeyChord(modifiers, vk);
            CapturedText = chord.ToString();

            DataContext = null;
            DataContext = this;
            e.Handled = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

