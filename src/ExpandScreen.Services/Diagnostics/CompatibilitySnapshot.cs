using System.Globalization;

namespace ExpandScreen.Services.Diagnostics
{
    public sealed class CompatibilitySnapshot
    {
        public DateTime TimestampUtc { get; set; }

        public string? AppVersion { get; set; }
        public string? AppInformationalVersion { get; set; }

        public string? OSDescription { get; set; }
        public string? OSArchitecture { get; set; }
        public string? ProcessArchitecture { get; set; }
        public string? FrameworkDescription { get; set; }
        public bool Is64BitProcess { get; set; }
        public int ProcessorCount { get; set; }

        public string? CurrentCulture { get; set; }
        public string? CurrentUICulture { get; set; }
        public string? InstalledUICulture { get; set; }

        public int? SystemDpi { get; set; }
        public double? SystemScale { get; set; }

        public List<DisplayDeviceInfo> DisplayDevices { get; set; } = new();
        public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();

        public sealed class DisplayDeviceInfo
        {
            public string? DeviceName { get; set; }
            public string? DeviceString { get; set; }
            public string? DeviceId { get; set; }
            public bool IsPrimary { get; set; }
            public bool IsAttachedToDesktop { get; set; }

            public int? WidthPx { get; set; }
            public int? HeightPx { get; set; }
            public int? BitsPerPixel { get; set; }
            public int? FrequencyHz { get; set; }
        }

        public sealed class NetworkAdapterInfo
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Type { get; set; }
            public string? Status { get; set; }
            public double? SpeedMbps { get; set; }
        }

        public static CompatibilitySnapshot CreateBase()
        {
            var snap = new CompatibilitySnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                CurrentCulture = CultureInfo.CurrentCulture.Name,
                CurrentUICulture = CultureInfo.CurrentUICulture.Name,
                InstalledUICulture = CultureInfo.InstalledUICulture.Name
            };
            return snap;
        }
    }
}

