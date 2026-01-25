using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Linq;
using ExpandScreen.Services.Configuration;
using ExpandScreen.Services.Security;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Diagnostics
{
    public static class SecuritySnapshotCollector
    {
        public static SecuritySnapshot Collect(AppConfig configSnapshot, string configPath)
        {
            var snap = new SecuritySnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                NetworkTlsEnabled = configSnapshot.Network.EnableTls,
                NetworkTcpPort = configSnapshot.Network.TcpPort,
                NetworkTimeoutMs = configSnapshot.Network.TimeoutMs,
                ConfigPath = configPath,
                LogDirectory = AppPaths.GetLogDirectory(),
                IsWindows = OperatingSystem.IsWindows(),
                IsAdministrator = IsAdministrator()
            };

            var entry = Assembly.GetEntryAssembly();
            var entryName = entry?.GetName();
            snap.AppVersion = entryName?.Version?.ToString();
            snap.AppInformationalVersion = entry?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            TryFillTlsInfo(snap);
            TryFillFirewallInfo(snap);

            return snap;
        }

        public static string BuildSummaryText(SecuritySnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ExpandScreen Security Snapshot");
            sb.AppendLine("============================");
            sb.AppendLine($"Time (UTC):           {snap.TimestampUtc:O}");
            sb.AppendLine($"App version:          {snap.AppVersion ?? "N/A"}");
            sb.AppendLine($"App info version:     {snap.AppInformationalVersion ?? "N/A"}");
            sb.AppendLine($"OS:                   {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Is administrator:     {(snap.IsWindows ? (snap.IsAdministrator ? "Yes" : "No") : "N/A")}");
            sb.AppendLine();

            sb.AppendLine("Network");
            sb.AppendLine("-------");
            sb.AppendLine($"WiFi TCP port:        {snap.NetworkTcpPort}");
            sb.AppendLine($"Timeout:              {snap.NetworkTimeoutMs} ms");
            sb.AppendLine($"TLS enabled:          {(snap.NetworkTlsEnabled ? "Yes" : "No")}");
            sb.AppendLine($"Pairing required:     {(snap.NetworkTlsEnabled && snap.TlsPairingCodeRequiredInHandshake ? "Yes (TLS only)" : "No")}");
            sb.AppendLine();

            sb.AppendLine("TLS pairing");
            sb.AppendLine("-----------");
            sb.AppendLine($"Certificate path:     {snap.TlsCertificatePath ?? "N/A"}");
            sb.AppendLine($"Cert file exists:     {(snap.TlsCertificateFileExists ? "Yes" : "No")}");
            if (snap.TlsCertificateFileSizeBytes.HasValue)
            {
                sb.AppendLine($"Cert file size:       {snap.TlsCertificateFileSizeBytes.Value} bytes");
            }
            if (snap.TlsCertificateLastWriteTimeUtc.HasValue)
            {
                sb.AppendLine($"Cert last write UTC:  {snap.TlsCertificateLastWriteTimeUtc.Value:O}");
            }
            sb.AppendLine($"SHA256 fingerprint:   {snap.TlsFingerprintSha256 ?? "N/A"}");
            sb.AppendLine($"Pairing code (mask):  {snap.TlsPairingCodeMasked ?? "N/A"}");
            sb.AppendLine();

            sb.AppendLine("Data & logs");
            sb.AppendLine("-----------");
            sb.AppendLine($"Config path:          {snap.ConfigPath ?? "N/A"}");
            sb.AppendLine($"Log directory:        {snap.LogDirectory ?? "N/A"}");
            sb.AppendLine();

            sb.AppendLine("Firewall (best-effort)");
            sb.AppendLine("----------------------");
            if (snap.Firewall == null || snap.Firewall.IsSupported == false)
            {
                sb.AppendLine("N/A");
            }
            else
            {
                sb.AppendLine($"Profile types:        {(snap.Firewall.CurrentProfileTypes?.ToString() ?? "N/A")}");
                sb.AppendLine($"Domain enabled:       {(snap.Firewall.DomainProfileEnabled?.ToString() ?? "N/A")}");
                sb.AppendLine($"Private enabled:      {(snap.Firewall.PrivateProfileEnabled?.ToString() ?? "N/A")}");
                sb.AppendLine($"Public enabled:       {(snap.Firewall.PublicProfileEnabled?.ToString() ?? "N/A")}");
                if (snap.Firewall.ExpandScreenRules.Count == 0)
                {
                    sb.AppendLine("ExpandScreen rules:   N/A");
                }
                else
                {
                    sb.AppendLine("ExpandScreen rules:");
                    foreach (var r in snap.Firewall.ExpandScreenRules.Take(20))
                    {
                        sb.Append("- ");
                        sb.Append(r.Name ?? "Unnamed");
                        if (!string.IsNullOrWhiteSpace(r.LocalPorts))
                        {
                            sb.Append($" ports={r.LocalPorts}");
                        }
                        if (!string.IsNullOrWhiteSpace(r.Protocol))
                        {
                            sb.Append($" proto={r.Protocol}");
                        }
                        if (r.Enabled.HasValue)
                        {
                            sb.Append($" enabled={r.Enabled.Value}");
                        }
                        sb.AppendLine();
                    }
                    if (snap.Firewall.ExpandScreenRules.Count > 20)
                    {
                        sb.AppendLine($"(truncated, total={snap.Firewall.ExpandScreenRules.Count})");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("Notes");
            sb.AppendLine("-----");
            sb.AppendLine("- Pairing code is masked in exports; copy the full code from Settings when needed.");
            sb.AppendLine("- Replay protection: non-increasing TCP message sequence numbers are rejected.");

            return sb.ToString();
        }

        private static void TryFillTlsInfo(SecuritySnapshot snap)
        {
            try
            {
                var manager = new TlsCertificateManager();
                snap.TlsCertificatePath = manager.CertificatePath;
                snap.TlsPairingCodeRequiredInHandshake = true;

                if (File.Exists(manager.CertificatePath))
                {
                    var info = new FileInfo(manager.CertificatePath);
                    snap.TlsCertificateFileExists = true;
                    snap.TlsCertificateFileSizeBytes = info.Length;
                    snap.TlsCertificateLastWriteTimeUtc = info.LastWriteTimeUtc;
                }

                using var cert = manager.GetOrCreateServerCertificate();
                snap.TlsFingerprintSha256 = TlsPairingCode.ToHexGrouped(TlsPairingCode.GetFingerprintSha256(cert));
                string code = TlsPairingCode.Get6DigitCode(cert);
                snap.TlsPairingCodeMasked = MaskPairingCode(code);
            }
            catch
            {
                snap.TlsPairingCodeRequiredInHandshake = true;
            }
        }

        private static void TryFillFirewallInfo(SecuritySnapshot snap)
        {
            if (!OperatingSystem.IsWindows())
            {
                snap.Firewall = new SecuritySnapshot.FirewallStatus { IsSupported = false };
                return;
            }

            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (type == null)
                {
                    snap.Firewall = new SecuritySnapshot.FirewallStatus { IsSupported = false };
                    return;
                }

                dynamic policy = Activator.CreateInstance(type)!;
                var firewall = new SecuritySnapshot.FirewallStatus { IsSupported = true };

                try
                {
                    firewall.CurrentProfileTypes = (int)policy.CurrentProfileTypes;
                }
                catch
                {
                }

                try
                {
                    firewall.DomainProfileEnabled = (bool)policy.FirewallEnabled[1];
                    firewall.PrivateProfileEnabled = (bool)policy.FirewallEnabled[2];
                    firewall.PublicProfileEnabled = (bool)policy.FirewallEnabled[4];
                }
                catch
                {
                }

                try
                {
                    dynamic rules = policy.Rules;
                    foreach (var ruleObj in rules)
                    {
                        dynamic rule = ruleObj;
                        string? name = null;
                        try
                        {
                            name = (string?)rule.Name;
                        }
                        catch
                        {
                        }

                        if (string.IsNullOrWhiteSpace(name) || !name.Contains("ExpandScreen", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        firewall.ExpandScreenRules.Add(new SecuritySnapshot.FirewallRuleInfo
                        {
                            Name = name,
                            ApplicationName = TryGetComString(rule, "ApplicationName"),
                            ServiceName = TryGetComString(rule, "ServiceName"),
                            Protocol = TryGetComString(rule, "Protocol"),
                            LocalPorts = TryGetComString(rule, "LocalPorts"),
                            Enabled = TryGetComBool(rule, "Enabled"),
                            Direction = TryGetComString(rule, "Direction"),
                            Action = TryGetComString(rule, "Action"),
                            Profiles = TryGetComString(rule, "Profiles")
                        });
                    }
                }
                catch
                {
                }

                snap.Firewall = firewall;
            }
            catch
            {
                snap.Firewall = new SecuritySnapshot.FirewallStatus { IsSupported = true };
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return false;
                }

                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static string MaskPairingCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "N/A";
            }

            string trimmed = code.Trim();
            if (trimmed.Length <= 2)
            {
                return new string('*', trimmed.Length);
            }

            return trimmed.Substring(0, 2) + new string('*', trimmed.Length - 2);
        }

        private static string? TryGetComString(dynamic comObject, string propertyName)
        {
            try
            {
                var value = comObject.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, comObject, null);
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryGetComBool(dynamic comObject, string propertyName)
        {
            try
            {
                return (bool?)comObject.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, comObject, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
