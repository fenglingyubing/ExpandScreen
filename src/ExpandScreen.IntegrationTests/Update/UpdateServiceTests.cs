using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpandScreen.Services.Update;
using Xunit;

namespace ExpandScreen.IntegrationTests.Update
{
    public sealed class UpdateServiceTests
    {
        [Fact]
        public async Task CheckAndDownload_FileManifest_Works()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ExpandScreen-UpdateServiceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string packagePath = Path.Combine(tempRoot, "ExpandScreen-2.0.0.exe");
            await File.WriteAllBytesAsync(packagePath, Encoding.UTF8.GetBytes("fake installer bytes"));

            string sha256 = await ComputeSha256HexLowerAsync(packagePath);

            var manifest = new
            {
                version = "2.0.0",
                downloadUrl = new Uri(packagePath).AbsoluteUri,
                sha256,
                releaseNotes = "Test release notes"
            };

            string manifestPath = Path.Combine(tempRoot, "latest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

            var service = new UpdateService(new UpdateServiceOptions(
                ManifestUri: new Uri(manifestPath),
                CurrentVersion: new Version(1, 0, 0)));

            UpdateCheckResult check = await service.CheckForUpdatesAsync();
            Assert.True(check.IsEnabled);
            Assert.True(check.IsUpdateAvailable);
            Assert.NotNull(check.Update);

            string downloadDir = Path.Combine(tempRoot, "downloads");
            DownloadedUpdate downloaded = await service.DownloadUpdateAsync(check.Update!, downloadDir);
            Assert.True(File.Exists(downloaded.FilePath));

            string downloadedHash = await ComputeSha256HexLowerAsync(downloaded.FilePath);
            Assert.Equal(sha256, downloadedHash);
        }

        [Fact]
        public async Task Check_RequiresSignature_WhenMissing_Fails()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ExpandScreen-UpdateServiceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string packagePath = Path.Combine(tempRoot, "ExpandScreen-2.0.0.exe");
            await File.WriteAllBytesAsync(packagePath, Encoding.UTF8.GetBytes("fake installer bytes"));

            string sha256 = await ComputeSha256HexLowerAsync(packagePath);

            var manifest = new
            {
                version = "2.0.0",
                downloadUrl = new Uri(packagePath).AbsoluteUri,
                sha256
            };

            string manifestPath = Path.Combine(tempRoot, "latest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

            using RSA rsa = RSA.Create(2048);
            string publicKeyPem = rsa.ExportRSAPublicKeyPem();

            var service = new UpdateService(new UpdateServiceOptions(
                ManifestUri: new Uri(manifestPath),
                CurrentVersion: new Version(1, 0, 0),
                TrustedManifestPublicKeyPem: publicKeyPem,
                RequireManifestSignature: true));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.CheckForUpdatesAsync());
        }

        [Fact]
        public async Task Check_SignatureVerification_Works()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ExpandScreen-UpdateServiceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string packagePath = Path.Combine(tempRoot, "ExpandScreen-2.0.0.exe");
            await File.WriteAllBytesAsync(packagePath, Encoding.UTF8.GetBytes("fake installer bytes"));

            string sha256 = await ComputeSha256HexLowerAsync(packagePath);
            string downloadUrl = new Uri(packagePath).AbsoluteUri;
            string version = "2.0.0";

            using RSA rsa = RSA.Create(2048);
            string publicKeyPem = rsa.ExportRSAPublicKeyPem();

            string payload = $"{version}\n{downloadUrl}\n{sha256}";
            byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var manifest = new
            {
                version,
                downloadUrl,
                sha256,
                signature = Convert.ToBase64String(signature)
            };

            string manifestPath = Path.Combine(tempRoot, "latest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

            var service = new UpdateService(new UpdateServiceOptions(
                ManifestUri: new Uri(manifestPath),
                CurrentVersion: new Version(1, 0, 0),
                TrustedManifestPublicKeyPem: publicKeyPem,
                RequireManifestSignature: true));

            UpdateCheckResult check = await service.CheckForUpdatesAsync();
            Assert.True(check.IsUpdateAvailable);
        }

        private static async Task<string> ComputeSha256HexLowerAsync(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            byte[] hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

