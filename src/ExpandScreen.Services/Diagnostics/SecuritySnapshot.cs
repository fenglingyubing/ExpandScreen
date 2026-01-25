namespace ExpandScreen.Services.Diagnostics
{
    public sealed class SecuritySnapshot
    {
        public DateTime TimestampUtc { get; set; }

        public string? AppVersion { get; set; }
        public string? AppInformationalVersion { get; set; }

        public bool NetworkTlsEnabled { get; set; }
        public int NetworkTcpPort { get; set; }
        public int NetworkTimeoutMs { get; set; }

        public string? ConfigPath { get; set; }
        public string? LogDirectory { get; set; }

        public bool IsWindows { get; set; }
        public bool IsAdministrator { get; set; }

        public string? TlsCertificatePath { get; set; }
        public bool TlsCertificateFileExists { get; set; }
        public long? TlsCertificateFileSizeBytes { get; set; }
        public DateTime? TlsCertificateLastWriteTimeUtc { get; set; }
        public string? TlsFingerprintSha256 { get; set; }
        public string? TlsPairingCodeMasked { get; set; }
        public bool TlsPairingCodeRequiredInHandshake { get; set; }

        public FirewallStatus? Firewall { get; set; }

        public sealed class FirewallStatus
        {
            public bool IsSupported { get; set; }
            public int? CurrentProfileTypes { get; set; }
            public bool? DomainProfileEnabled { get; set; }
            public bool? PrivateProfileEnabled { get; set; }
            public bool? PublicProfileEnabled { get; set; }
            public List<FirewallRuleInfo> ExpandScreenRules { get; set; } = new();
        }

        public sealed class FirewallRuleInfo
        {
            public string? Name { get; set; }
            public string? ApplicationName { get; set; }
            public string? ServiceName { get; set; }
            public string? Protocol { get; set; }
            public string? LocalPorts { get; set; }
            public bool? Enabled { get; set; }
            public string? Direction { get; set; }
            public string? Action { get; set; }
            public string? Profiles { get; set; }
        }
    }
}

