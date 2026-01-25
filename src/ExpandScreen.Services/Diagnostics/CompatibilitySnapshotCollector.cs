using System.Globalization;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ExpandScreen.Services.Diagnostics
{
    public static class CompatibilitySnapshotCollector
    {
        public static CompatibilitySnapshot Collect()
        {
            var snap = CompatibilitySnapshot.CreateBase();

            var entry = Assembly.GetEntryAssembly();
            var entryName = entry?.GetName();
            snap.AppVersion = entryName?.Version?.ToString();
            snap.AppInformationalVersion = entry?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            snap.OSDescription = RuntimeInformation.OSDescription;
            snap.OSArchitecture = RuntimeInformation.OSArchitecture.ToString();
            snap.ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
            snap.FrameworkDescription = RuntimeInformation.FrameworkDescription;
            snap.Is64BitProcess = Environment.Is64BitProcess;
            snap.ProcessorCount = Environment.ProcessorCount;

            TryFillDpi(snap);
            TryFillDisplayDevices(snap);
            TryFillNetworkAdapters(snap);

            return snap;
        }

        public static string BuildSummaryText(CompatibilitySnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ExpandScreen Compatibility Snapshot");
            sb.AppendLine("=================================");
            sb.AppendLine($"Time (UTC):           {snap.TimestampUtc:O}");
            sb.AppendLine($"App version:          {snap.AppVersion ?? "N/A"}");
            sb.AppendLine($"App info version:     {snap.AppInformationalVersion ?? "N/A"}");
            sb.AppendLine($"OS:                   {snap.OSDescription ?? "N/A"}");
            sb.AppendLine($"OS arch:              {snap.OSArchitecture ?? "N/A"}");
            sb.AppendLine($"Process arch:         {snap.ProcessArchitecture ?? "N/A"}");
            sb.AppendLine($"Framework:            {snap.FrameworkDescription ?? "N/A"}");
            sb.AppendLine($"CPU cores:            {snap.ProcessorCount}");
            sb.AppendLine($"Culture:              {snap.CurrentCulture ?? CultureInfo.CurrentCulture.Name}");
            sb.AppendLine($"UI culture:           {snap.CurrentUICulture ?? CultureInfo.CurrentUICulture.Name}");
            sb.AppendLine($"Installed UI culture: {snap.InstalledUICulture ?? CultureInfo.InstalledUICulture.Name}");
            sb.AppendLine(
                $"System DPI / scale:   {(snap.SystemDpi.HasValue ? snap.SystemDpi.Value.ToString(CultureInfo.InvariantCulture) : "N/A")} / " +
                $"{(snap.SystemScale.HasValue ? snap.SystemScale.Value.ToString("0.##", CultureInfo.InvariantCulture) + "x" : "N/A")}");

            sb.AppendLine();
            sb.AppendLine("Displays");
            sb.AppendLine("--------");
            if (snap.DisplayDevices.Count == 0)
            {
                sb.AppendLine("N/A");
            }
            else
            {
                foreach (var d in snap.DisplayDevices)
                {
                    sb.Append("- ");
                    sb.Append(d.DeviceName ?? "Unknown");
                    if (d.IsPrimary)
                    {
                        sb.Append(" (Primary)");
                    }

                    if (d.WidthPx.HasValue && d.HeightPx.HasValue)
                    {
                        sb.Append($" {d.WidthPx.Value}x{d.HeightPx.Value}");
                    }

                    if (d.FrequencyHz.HasValue)
                    {
                        sb.Append($" @{d.FrequencyHz.Value}Hz");
                    }

                    if (!string.IsNullOrWhiteSpace(d.DeviceString))
                    {
                        sb.Append($" — {d.DeviceString}");
                    }

                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine("Network");
            sb.AppendLine("-------");
            if (snap.NetworkAdapters.Count == 0)
            {
                sb.AppendLine("N/A");
            }
            else
            {
                foreach (var n in snap.NetworkAdapters)
                {
                    sb.Append("- ");
                    sb.Append(n.Name ?? "Unknown");
                    if (!string.IsNullOrWhiteSpace(n.Type))
                    {
                        sb.Append($" ({n.Type})");
                    }

                    if (!string.IsNullOrWhiteSpace(n.Status))
                    {
                        sb.Append($" {n.Status}");
                    }

                    if (n.SpeedMbps.HasValue)
                    {
                        sb.Append($" {n.SpeedMbps.Value:0.#} Mbps");
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static void TryFillNetworkAdapters(CompatibilitySnapshot snap)
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    double? speedMbps = null;
                    try
                    {
                        if (nic.Speed > 0)
                        {
                            speedMbps = nic.Speed / 1_000_000.0;
                        }
                    }
                    catch
                    {
                        speedMbps = null;
                    }

                    snap.NetworkAdapters.Add(new CompatibilitySnapshot.NetworkAdapterInfo
                    {
                        Name = nic.Name,
                        Description = nic.Description,
                        Type = nic.NetworkInterfaceType.ToString(),
                        Status = nic.OperationalStatus.ToString(),
                        SpeedMbps = speedMbps
                    });
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static void TryFillDpi(CompatibilitySnapshot snap)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

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

        private static void TryFillDisplayDevices(CompatibilitySnapshot snap)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                for (uint i = 0; ; i++)
                {
                    var dd = new DISPLAY_DEVICE
                    {
                        cb = Marshal.SizeOf<DISPLAY_DEVICE>(),
                        DeviceName = new string('\0', 32),
                        DeviceString = new string('\0', 128),
                        DeviceID = new string('\0', 128),
                        DeviceKey = new string('\0', 128)
                    };

                    if (!EnumDisplayDevices(null, i, ref dd, 0))
                    {
                        break;
                    }

                    bool attachedToDesktop = (dd.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) != 0;
                    bool primary = (dd.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) != 0;

                    int? width = null;
                    int? height = null;
                    int? bpp = null;
                    int? freq = null;

                    try
                    {
                        var mode = new DEVMODE
                        {
                            dmDeviceName = new string('\0', 32),
                            dmFormName = new string('\0', 32),
                            dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
                        };

                        string deviceName = (dd.DeviceName ?? string.Empty).TrimEnd('\0');
                        if (!string.IsNullOrWhiteSpace(deviceName) && EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode))
                        {
                            width = (int)mode.dmPelsWidth;
                            height = (int)mode.dmPelsHeight;
                            bpp = (int)mode.dmBitsPerPel;
                            freq = (int)mode.dmDisplayFrequency;
                        }
                    }
                    catch
                    {
                        // best-effort
                    }

                    snap.DisplayDevices.Add(new CompatibilitySnapshot.DisplayDeviceInfo
                    {
                        DeviceName = (dd.DeviceName ?? string.Empty).TrimEnd('\0'),
                        DeviceString = (dd.DeviceString ?? string.Empty).TrimEnd('\0'),
                        DeviceId = (dd.DeviceID ?? string.Empty).TrimEnd('\0'),
                        IsPrimary = primary,
                        IsAttachedToDesktop = attachedToDesktop,
                        WidthPx = width,
                        HeightPx = height,
                        BitsPerPixel = bpp,
                        FrequencyHz = freq
                    });
                }
            }
            catch
            {
                // best-effort
            }
        }

        private const int ENUM_CURRENT_SETTINGS = -1;

        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x00000001,
            PrimaryDevice = 0x00000004
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public DisplayDeviceStateFlags StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;

            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;

            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;

            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;

            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll")]
        private static extern int GetDpiForSystem();
    }
}
