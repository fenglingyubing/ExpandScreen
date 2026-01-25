using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Utils;
using ExpandScreen.Utils.Hotkeys;

namespace ExpandScreen.UI.Services
{
    public enum HotkeyAction
    {
        ToggleMainWindow,
        ConnectDisconnect,
        NextDevice,
        TogglePerformanceMode
    }

    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WmHotkey = 0x0312;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;

        private readonly IntPtr _windowHandle;
        private readonly HwndSource _hwndSource;
        private readonly Dictionary<int, HotkeyAction> _idToAction = new();
        private readonly Dictionary<HotkeyAction, HotkeyChord> _actionToChord = new();
        private bool _disposed;

        public event EventHandler<HotkeyAction>? HotkeyPressed;

        public GlobalHotkeyService(Window window)
        {
            _windowHandle = new WindowInteropHelper(window).Handle;
            _hwndSource = HwndSource.FromHwnd(_windowHandle) ?? throw new InvalidOperationException("HwndSource not available.");
            _hwndSource.AddHook(WndProc);
        }

        public IReadOnlyList<string> ApplyConfig(AppConfig config)
        {
            if (_disposed)
            {
                return Array.Empty<string>();
            }

            UnregisterAll();

            if (!OperatingSystem.IsWindows())
            {
                return new[] { "global hotkeys disabled: not running on Windows." };
            }

            if (!config.Hotkeys.Enabled)
            {
                return Array.Empty<string>();
            }

            var warnings = new List<string>();
            var defaults = AppConfig.CreateDefault().Hotkeys;

            TryRegister(HotkeyAction.ToggleMainWindow, config.Hotkeys.ToggleMainWindow ?? defaults.ToggleMainWindow, warnings);
            TryRegister(HotkeyAction.ConnectDisconnect, config.Hotkeys.ConnectDisconnect ?? defaults.ConnectDisconnect, warnings);
            TryRegister(HotkeyAction.NextDevice, config.Hotkeys.NextDevice ?? defaults.NextDevice, warnings);
            TryRegister(HotkeyAction.TogglePerformanceMode, config.Hotkeys.TogglePerformanceMode ?? defaults.TogglePerformanceMode, warnings);

            return warnings;
        }

        private void TryRegister(HotkeyAction action, string? chordText, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(chordText))
            {
                return;
            }

            if (!HotkeyChord.TryParse(chordText, out var chord) || chord.IsEmpty)
            {
                warnings.Add($"hotkeys.{action} invalid; skipped.");
                return;
            }

            if (chord.Modifiers == HotkeyModifiers.None)
            {
                warnings.Add($"hotkeys.{action} requires modifier; skipped.");
                return;
            }

            foreach (var existing in _actionToChord)
            {
                if (existing.Value.Equals(chord))
                {
                    warnings.Add($"hotkeys.{action} conflicts with {existing.Key}; skipped.");
                    return;
                }
            }

            int id = 0x5200 + _idToAction.Count;
            uint modifiers = ToNativeModifiers(chord.Modifiers);

            bool ok = RegisterHotKey(_windowHandle, id, modifiers, (uint)chord.VirtualKey);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                LogHelper.Warning($"RegisterHotKey failed for {action} ({chord}) win32={err}.");
                warnings.Add($"hotkeys.{action} unavailable (win32={err}).");
                return;
            }

            _idToAction[id] = action;
            _actionToChord[action] = chord;
        }

        private static uint ToNativeModifiers(HotkeyModifiers modifiers)
        {
            uint value = 0;
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) value |= ModAlt;
            if (modifiers.HasFlag(HotkeyModifiers.Control)) value |= ModControl;
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) value |= ModShift;
            if (modifiers.HasFlag(HotkeyModifiers.Windows)) value |= ModWin;
            return value;
        }

        private void UnregisterAll()
        {
            foreach (var id in _idToAction.Keys.ToList())
            {
                try
                {
                    UnregisterHotKey(_windowHandle, id);
                }
                catch
                {
                }
            }

            _idToAction.Clear();
            _actionToChord.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey)
            {
                return IntPtr.Zero;
            }

            int id = wParam.ToInt32();
            if (_idToAction.TryGetValue(id, out var action))
            {
                HotkeyPressed?.Invoke(this, action);
                handled = true;
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                UnregisterAll();
            }
            catch
            {
            }

            try
            {
                _hwndSource.RemoveHook(WndProc);
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}

