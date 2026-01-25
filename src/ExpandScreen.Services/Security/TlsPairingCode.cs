using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ExpandScreen.Services.Security
{
    public static class TlsPairingCode
    {
        public static byte[] GetFingerprintSha256(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            return SHA256.HashData(certificate.RawData);
        }

        public static string Compute6DigitCode(byte[] sha256Fingerprint)
        {
            if (sha256Fingerprint == null) throw new ArgumentNullException(nameof(sha256Fingerprint));
            if (sha256Fingerprint.Length < 4) throw new ArgumentException("Fingerprint must be at least 4 bytes.", nameof(sha256Fingerprint));

            uint value =
                ((uint)sha256Fingerprint[0] << 24)
                | ((uint)sha256Fingerprint[1] << 16)
                | ((uint)sha256Fingerprint[2] << 8)
                | sha256Fingerprint[3];

            return (value % 1_000_000u).ToString("D6");
        }

        public static string Get6DigitCode(X509Certificate2 certificate)
        {
            return Compute6DigitCode(GetFingerprintSha256(certificate));
        }

        public static string ToHex(byte[] bytes)
        {
            return Convert.ToHexString(bytes);
        }

        public static string ToHexGrouped(byte[] bytes, int groupSize = 2, string separator = ":")
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (groupSize <= 0) throw new ArgumentOutOfRangeException(nameof(groupSize));

            string hex = Convert.ToHexString(bytes);
            var groups = new List<string>(hex.Length / (groupSize * 2));
            for (int i = 0; i < hex.Length; i += groupSize * 2)
            {
                groups.Add(hex.Substring(i, Math.Min(groupSize * 2, hex.Length - i)));
            }
            return string.Join(separator, groups);
        }
    }
}

