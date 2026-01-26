using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ExpandScreen.Services.Configuration;

namespace ExpandScreen.Services.Diagnostics
{
    public static class UxSnapshotCollector
    {
        // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfoa
        private const uint SPI_GETSCREENREADER = 0x0046;
        private const uint SPI_GETHIGHCONTRAST = 0x0042;
        private const uint SPI_GETKEYBOARDCUES = 0x100A;
        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
        private const uint SPI_GETUIEFFECTS = 0x103E;

        private const uint HCF_HIGHCONTRASTON = 0x00000001;

        public static UxSnapshot Collect(AppConfig configSnapshot)
        {
            var snap = new UxSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                ConfigTheme = configSnapshot.General.Theme,
                IsWindows = OperatingSystem.IsWindows()
            };

            var entry = Assembly.GetEntryAssembly();
            var entryName = entry?.GetName();
            snap.AppVersion = entryName?.Version?.ToString();
            snap.AppInformationalVersion = entry?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (snap.IsWindows)
            {
                TryFillDpi(snap);
                TryFillHighContrast(snap);
                snap.ScreenReaderPresent = TryGetBool(SPI_GETSCREENREADER);
                snap.KeyboardCuesEnabled = TryGetBool(SPI_GETKEYBOARDCUES);
                snap.ClientAreaAnimationEnabled = TryGetBool(SPI_GETCLIENTAREAANIMATION);
                snap.UiEffectsEnabled = TryGetBool(SPI_GETUIEFFECTS);
            }

            return snap;
        }

        public static string BuildSummaryText(UxSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ExpandScreen UX Snapshot");
            sb.AppendLine("=======================");
            sb.AppendLine($"Time (UTC):            {snap.TimestampUtc:O}");
            sb.AppendLine($"App version:           {snap.AppVersion ?? "N/A"}");
            sb.AppendLine($"App info version:      {snap.AppInformationalVersion ?? "N/A"}");
            sb.AppendLine($"Config theme:          {snap.ConfigTheme}");
            sb.AppendLine($"System DPI / scale:    {(snap.SystemDpi.HasValue ? snap.SystemDpi.Value.ToString() : "N/A")} / {(snap.SystemScale.HasValue ? snap.SystemScale.Value.ToString("0.##") + "x" : "N/A")}");
            sb.AppendLine();

            sb.AppendLine("Accessibility (best-effort)");
            sb.AppendLine("---------------------------");
            sb.AppendLine($"High contrast:         {FormatBool(snap.HighContrastEnabled)}");
            sb.AppendLine($"High contrast scheme:  {snap.HighContrastScheme ?? "N/A"}");
            sb.AppendLine($"Screen reader present: {FormatBool(snap.ScreenReaderPresent)}");
            sb.AppendLine($"Keyboard cues:         {FormatBool(snap.KeyboardCuesEnabled)}");
            sb.AppendLine();

            sb.AppendLine("Motion & effects (best-effort)");
            sb.AppendLine("------------------------------");
            sb.AppendLine($"Client area animation: {FormatBool(snap.ClientAreaAnimationEnabled)}");
            sb.AppendLine($"UI effects:            {FormatBool(snap.UiEffectsEnabled)}");

            return sb.ToString();
        }

        public static string BuildFeedbackTemplate(UxSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ExpandScreen 用户体验测试反馈");
            sb.AppendLine();
            sb.AppendLine("## 测试环境（粘贴/自动生成）");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(BuildSummaryText(snap).TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();

            sb.AppendLine("## 关键流程（易用性）");
            sb.AppendLine();
            sb.AppendLine("- [ ] 首次使用流程：是否清晰，是否有阻塞点");
            sb.AppendLine("- [ ] 连接流程：步骤是否过多，默认值是否合理");
            sb.AppendLine("- [ ] 错误提示：是否可理解、是否给出下一步建议");
            sb.AppendLine("- [ ] 帮助文档：是否容易找到、是否能解决常见问题");
            sb.AppendLine();

            sb.AppendLine("## UI（视觉与交互）");
            sb.AppendLine();
            sb.AppendLine("- [ ] 布局：信息层级是否清晰，按钮是否易找");
            sb.AppendLine("- [ ] 响应：点击/切换页面是否卡顿");
            sb.AppendLine("- [ ] 动画：是否流畅，是否有“减少动态”需求");
            sb.AppendLine("- [ ] 深色模式：对比度/可读性/控件一致性");
            sb.AppendLine("- [ ] 高对比度：可读性/边界/焦点提示是否足够");
            sb.AppendLine();

            sb.AppendLine("## 无障碍（Accessibility）");
            sb.AppendLine();
            sb.AppendLine("- [ ] 屏幕阅读器：主要控件/按钮是否可被朗读与识别");
            sb.AppendLine("- [ ] 键盘导航：Tab 顺序、焦点可见性、快捷键");
            sb.AppendLine("- [ ] 字体缩放：100%/125%/150%/200% 是否布局崩坏");
            sb.AppendLine();

            sb.AppendLine("## 发现的问题（可复现步骤）");
            sb.AppendLine();
            sb.AppendLine("1.");
            sb.AppendLine();
            sb.AppendLine("## 改进建议");
            sb.AppendLine();
            sb.AppendLine("-");
            return sb.ToString();
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "Yes" : "No") : "N/A";
        }

        private static void TryFillDpi(UxSnapshot snap)
        {
            try
            {
                int dpi = GetDpiForSystem();
                if (dpi > 0)
                {
                    snap.SystemDpi = dpi;
                    snap.SystemScale = dpi / 96.0;
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static void TryFillHighContrast(UxSnapshot snap)
        {
            try
            {
                var hc = new HIGHCONTRAST
                {
                    cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>(),
                    lpszDefaultScheme = IntPtr.Zero
                };

                if (!SystemParametersInfo(SPI_GETHIGHCONTRAST, hc.cbSize, ref hc, 0))
                {
                    return;
                }

                snap.HighContrastEnabled = (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;
                if (hc.lpszDefaultScheme != IntPtr.Zero)
                {
                    snap.HighContrastScheme = Marshal.PtrToStringUni(hc.lpszDefaultScheme);
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static bool? TryGetBool(uint action)
        {
            try
            {
                uint value = 0;
                return SystemParametersInfo(action, 0, ref value, 0) ? value != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct HIGHCONTRAST
        {
            public uint cbSize;
            public uint dwFlags;
            public IntPtr lpszDefaultScheme;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        private static extern int GetDpiForSystem();
    }
}

