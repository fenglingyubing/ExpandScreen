using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExpandScreen.Services.Update
{
    public sealed class UpdateService
    {
        private readonly UpdateServiceOptions _options;
        private readonly HttpClient _httpClient;

        public UpdateService(UpdateServiceOptions options, HttpClient? httpClient = null)
        {
            _options = options;
            _httpClient = httpClient ?? CreateDefaultHttpClient();
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            if (_options.ManifestUri is null)
            {
                return UpdateCheckResult.Disabled();
            }

            string manifestJson = await ReadManifestJsonAsync(_options.ManifestUri, cancellationToken).ConfigureAwait(false);

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                manifestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest is null)
            {
                throw new InvalidOperationException("Invalid update manifest: empty JSON.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new InvalidOperationException("Invalid update manifest: missing version.");
            }

            if (!Version.TryParse(manifest.Version, out var latestVersion))
            {
                throw new InvalidOperationException($"Invalid update manifest: unsupported version '{manifest.Version}'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                throw new InvalidOperationException("Invalid update manifest: missing downloadUrl.");
            }

            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
            {
                throw new InvalidOperationException($"Invalid update manifest: bad downloadUrl '{manifest.DownloadUrl}'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                throw new InvalidOperationException("Invalid update manifest: missing sha256.");
            }

            string sha256 = NormalizeSha256(manifest.Sha256);

            if (!VerifyManifestIfConfigured(manifest, sha256))
            {
                throw new InvalidOperationException("Update manifest signature verification failed.");
            }

            if (latestVersion <= _options.CurrentVersion)
            {
                return UpdateCheckResult.NoUpdate();
            }

            return UpdateCheckResult.HasUpdate(new UpdateInfo(
                LatestVersion: latestVersion,
                DownloadUri: downloadUri,
                Sha256HexLower: sha256,
                ReleaseNotes: manifest.ReleaseNotes
            ));
        }

        public async Task<DownloadedUpdate> DownloadUpdateAsync(
            UpdateInfo update,
            string destinationDirectory,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException("Destination directory must not be empty.", nameof(destinationDirectory));
            }

            Directory.CreateDirectory(destinationDirectory);

            string fileName = TryGetFileNameFromUri(update.DownloadUri) ?? $"ExpandScreen-{update.LatestVersion}.bin";
            string destinationPath = Path.Combine(destinationDirectory, fileName);

            await DownloadToFileAsync(update.DownloadUri, destinationPath, progress, cancellationToken).ConfigureAwait(false);

            string computedHash = await ComputeSha256HexLowerAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(computedHash, update.Sha256HexLower, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(destinationPath);
                throw new InvalidOperationException($"SHA256 mismatch. Expected '{update.Sha256HexLower}', got '{computedHash}'.");
            }

            return new DownloadedUpdate(update, destinationPath);
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ExpandScreen-Updater/1.0");
            return client;
        }

        private static string? TryGetFileNameFromUri(Uri uri)
        {
            if (uri.IsFile)
            {
                var name = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }

            var candidate = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
        }

        private async Task<string> ReadManifestJsonAsync(Uri manifestUri, CancellationToken cancellationToken)
        {
            if (manifestUri.IsFile)
            {
                return await File.ReadAllTextAsync(manifestUri.LocalPath, cancellationToken).ConfigureAwait(false);
            }

            return await _httpClient.GetStringAsync(manifestUri, cancellationToken).ConfigureAwait(false);
        }

        private async Task DownloadToFileAsync(
            Uri uri,
            string destinationPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            TryDelete(destinationPath);

            if (uri.IsFile)
            {
                await using var source = File.OpenRead(uri.LocalPath);
                await using var destination = File.Create(destinationPath);
                await CopyWithProgressAsync(source, destination, source.Length, progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(destinationPath);

            await CopyWithProgressAsync(stream, fileStream, contentLength, progress, cancellationToken).ConfigureAwait(false);
        }

        private static async Task CopyWithProgressAsync(
            Stream source,
            Stream destination,
            long? totalBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024 * 64];
            long copied = 0;

            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    progress?.Report(1.0);
                    return;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progress?.Report(Math.Clamp((double)copied / totalBytes.Value, 0.0, 1.0));
                }
                else
                {
                    progress?.Report(0.0);
                }
            }
        }

        private static async Task<string> ComputeSha256HexLowerAsync(string filePath, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(filePath);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private bool VerifyManifestIfConfigured(UpdateManifest manifest, string normalizedSha256HexLower)
        {
            if (_options.TrustedManifestPublicKeyPem is null && !_options.RequireManifestSignature)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_options.TrustedManifestPublicKeyPem))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.Signature))
            {
                return !_options.RequireManifestSignature;
            }

            string payload = $"{manifest.Version}\n{manifest.DownloadUrl}\n{normalizedSha256HexLower}";
            byte[] data = Encoding.UTF8.GetBytes(payload);

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(manifest.Signature);
            }
            catch
            {
                return false;
            }

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(_options.TrustedManifestPublicKeyPem);

            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static string NormalizeSha256(string sha256)
        {
            string normalized = sha256.Trim().ToLowerInvariant();
            if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["sha256:".Length..].Trim();
            }

            normalized = normalized.Replace(" ", string.Empty);
            if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidOperationException($"Invalid sha256 '{sha256}'.");
            }

            return normalized;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore best-effort cleanup
            }
        }
    }
}
