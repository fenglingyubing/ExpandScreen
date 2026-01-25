using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Security
{
    public sealed class TlsCertificateManager
    {
        private const string DefaultCertificateSubject = "CN=ExpandScreen";
        private const string CertificateFileName = "wifi-server.pfx.dpapi";

        public string CertificatePath { get; }

        public TlsCertificateManager(string? certificatePath = null)
        {
            CertificatePath = string.IsNullOrWhiteSpace(certificatePath)
                ? Path.Combine(AppPaths.GetLocalAppDataDirectory(), "security", CertificateFileName)
                : certificatePath!;
        }

        public X509Certificate2 GetOrCreateServerCertificate(string subject = DefaultCertificateSubject)
        {
            var loaded = TryLoadCertificate();
            if (loaded != null)
            {
                return loaded;
            }

            var created = CreateSelfSignedServerCertificate(subject);
            TrySaveCertificate(created);
            return created;
        }

        public bool RotateCertificate()
        {
            try
            {
                if (File.Exists(CertificatePath))
                {
                    File.Delete(CertificatePath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private X509Certificate2? TryLoadCertificate()
        {
            try
            {
                if (!File.Exists(CertificatePath))
                {
                    return null;
                }

                byte[] stored = File.ReadAllBytes(CertificatePath);
                byte[] pfxBytes = TryUnprotect(stored) ?? stored;

                return new X509Certificate2(
                    pfxBytes,
                    (string?)null,
                    X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            }
            catch
            {
                return null;
            }
        }

        private void TrySaveCertificate(X509Certificate2 certificate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CertificatePath)!);
                byte[] pfx = certificate.Export(X509ContentType.Pfx);
                byte[] stored = TryProtect(pfx) ?? pfx;
                File.WriteAllBytes(CertificatePath, stored);
            }
            catch
            {
                // best-effort persistence
            }
        }

        private static X509Certificate2 CreateSelfSignedServerCertificate(string subject)
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName(subject),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new("1.3.6.1.5.5.7.3.1") // Server Authentication
                    },
                    critical: true));

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddYears(5);

            using var cert = request.CreateSelfSigned(notBefore, notAfter);

            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx),
                (string?)null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        }

        private static byte[]? TryProtect(byte[] data)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return null;
                }

                return System.Security.Cryptography.ProtectedData.Protect(
                    data,
                    optionalEntropy: null,
                    scope: System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? TryUnprotect(byte[] data)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return null;
                }

                return System.Security.Cryptography.ProtectedData.Unprotect(
                    data,
                    optionalEntropy: null,
                    scope: System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }
    }
}
