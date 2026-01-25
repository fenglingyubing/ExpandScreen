using System.Text.Json;
using System.Text.Json.Serialization;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Configuration
{
    public sealed class ConfigChangedEventArgs : EventArgs
    {
        public ConfigChangedEventArgs(AppConfig config, IReadOnlyList<string> warnings)
        {
            Config = config;
            Warnings = warnings;
        }

        public AppConfig Config { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class ConfigSaveResult
    {
        public ConfigSaveResult(AppConfig config, IReadOnlyList<string> warnings)
        {
            Config = config;
            Warnings = warnings;
        }

        public AppConfig Config { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class ConfigService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        private readonly SemaphoreSlim _lock = new(1, 1);
        private AppConfig _current = AppConfig.CreateDefault();

        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private volatile int _reloadScheduled;

        public ConfigService(string? configPath = null)
        {
            ConfigPath = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : configPath!;
        }

        public string ConfigPath { get; }

        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

        public AppConfig GetSnapshot()
        {
            _lock.Wait();
            try
            {
                return Clone(_current);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var (config, warnings) = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
                _current = config;
                if (warnings.Count > 0)
                {
                    LogHelper.Warning($"Config loaded with {warnings.Count} warning(s): {string.Join("; ", warnings)}");
                }

                return Clone(_current);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ConfigSaveResult> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var normalized = Clone(config);
                var warnings = NormalizeInPlace(normalized);

                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                string json = JsonSerializer.Serialize(normalized, JsonOptions);
                await File.WriteAllTextAsync(ConfigPath, json, cancellationToken).ConfigureAwait(false);

                bool changed = !StableJsonEquals(_current, normalized);
                _current = normalized;

                if (changed)
                {
                    ConfigChanged?.Invoke(this, new ConfigChangedEventArgs(Clone(_current), warnings));
                }

                return new ConfigSaveResult(Clone(_current), warnings);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ConfigSaveResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        {
            return await SaveAsync(AppConfig.CreateDefault(), cancellationToken).ConfigureAwait(false);
        }

        public void StartWatching()
        {
            if (_watcher != null)
            {
                return;
            }

            string directory = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(directory);

            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                Filter = Path.GetFileName(ConfigPath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName
            };

            watcher.Changed += OnWatcherEvent;
            watcher.Created += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }

        public void StopWatching()
        {
            if (_watcher == null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherEvent;
            _watcher.Created -= OnWatcherEvent;
            _watcher.Renamed -= OnWatcherEvent;
            _watcher.Dispose();
            _watcher = null;
        }

        private void OnWatcherEvent(object sender, FileSystemEventArgs e)
        {
            if (Interlocked.Exchange(ref _reloadScheduled, 1) == 1)
            {
                return;
            }

            _debounceTimer ??= new Timer(_ =>
            {
                Interlocked.Exchange(ref _reloadScheduled, 0);
                try
                {
                    _ = ReloadFromDiskIfChangedAsync();
                }
                catch
                {
                    // ignore
                }
            }, null, Timeout.Infinite, Timeout.Infinite);

            _debounceTimer.Change(250, Timeout.Infinite);
        }

        private async Task ReloadFromDiskIfChangedAsync()
        {
            try
            {
                await _lock.WaitAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            try
            {
                var (loaded, warnings) = await LoadCoreAsync(CancellationToken.None).ConfigureAwait(false);
                if (StableJsonEquals(_current, loaded))
                {
                    return;
                }

                _current = loaded;
                LogHelper.Info("Config hot-reloaded from disk.");
                ConfigChanged?.Invoke(this, new ConfigChangedEventArgs(Clone(_current), warnings));
            }
            catch (Exception ex)
            {
                LogHelper.Error("Failed to hot-reload config.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<(AppConfig Config, List<string> Warnings)> LoadCoreAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            if (!File.Exists(ConfigPath))
            {
                var created = AppConfig.CreateDefault();
                var warnings = NormalizeInPlace(created);
                string json = JsonSerializer.Serialize(created, JsonOptions);
                await File.WriteAllTextAsync(ConfigPath, json, cancellationToken).ConfigureAwait(false);
                return (created, warnings);
            }

            string text = await File.ReadAllTextAsync(ConfigPath, cancellationToken).ConfigureAwait(false);

            AppConfig? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions);
            }
            catch (Exception ex)
            {
                LogHelper.Error("Config JSON parse failed; resetting to defaults.", ex);
            }

            if (parsed == null)
            {
                string badPath = $"{ConfigPath}.bad-{DateTime.UtcNow:yyyyMMddHHmmss}";
                TryMove(ConfigPath, badPath);
                var reset = AppConfig.CreateDefault();
                var resetWarnings = NormalizeInPlace(reset);
                string json = JsonSerializer.Serialize(reset, JsonOptions);
                await File.WriteAllTextAsync(ConfigPath, json, cancellationToken).ConfigureAwait(false);
                return (reset, resetWarnings);
            }

            var warnings2 = NormalizeInPlace(parsed);
            return (parsed, warnings2);
        }

        private static List<string> NormalizeInPlace(AppConfig config)
        {
            var warnings = new List<string>();

            config.General ??= new GeneralConfig();
            config.Video ??= new VideoConfig();
            config.Audio ??= new AudioConfig();
            config.Network ??= new NetworkConfig();
            config.Performance ??= new PerformanceConfig();
            config.Hotkeys ??= new HotkeysConfig();
            config.Update ??= new UpdateConfig();
            config.Logging ??= new LoggingConfig();

            if (config.Video.Width < 320)
            {
                warnings.Add("video.width too small; clamped to 320.");
                config.Video.Width = 320;
            }

            if (config.Video.Height < 240)
            {
                warnings.Add("video.height too small; clamped to 240.");
                config.Video.Height = 240;
            }

            if (config.Video.FrameRate < 1 || config.Video.FrameRate > 240)
            {
                warnings.Add("video.frameRate out of range; clamped to 1-240.");
                config.Video.FrameRate = Math.Clamp(config.Video.FrameRate, 1, 240);
            }

            if (config.Video.BitrateBps < 250_000)
            {
                warnings.Add("video.bitrateBps too small; clamped to 250000.");
                config.Video.BitrateBps = 250_000;
            }

            // Audio
            if (config.Audio.SampleRate is not (8000 or 12000 or 16000 or 24000 or 48000))
            {
                warnings.Add("audio.sampleRate invalid; reset to 48000.");
                config.Audio.SampleRate = 48000;
            }

            if (config.Audio.Channels is < 1 or > 2)
            {
                warnings.Add("audio.channels out of range; clamped to 1-2.");
                config.Audio.Channels = Math.Clamp(config.Audio.Channels, 1, 2);
            }

            if (config.Audio.FrameDurationMs is not (10 or 20 or 40 or 60))
            {
                warnings.Add("audio.frameDurationMs invalid; reset to 20.");
                config.Audio.FrameDurationMs = 20;
            }

            if (config.Audio.BitrateBps < 6000)
            {
                warnings.Add("audio.bitrateBps too small; clamped to 6000.");
                config.Audio.BitrateBps = 6000;
            }

            if (config.Audio.BitrateBps > 512_000)
            {
                warnings.Add("audio.bitrateBps too large; clamped to 512000.");
                config.Audio.BitrateBps = 512_000;
            }

            if (config.Network.TcpPort < 1024 || config.Network.TcpPort > 65535)
            {
                warnings.Add("network.tcpPort out of range; reset to default.");
                config.Network.TcpPort = new NetworkConfig().TcpPort;
            }

            if (config.Network.TimeoutMs < 100 || config.Network.TimeoutMs > 120_000)
            {
                warnings.Add("network.timeoutMs out of range; clamped to 100-120000.");
                config.Network.TimeoutMs = Math.Clamp(config.Network.TimeoutMs, 100, 120_000);
            }

            if (config.Network.ReconnectAttempts < 0 || config.Network.ReconnectAttempts > 100)
            {
                warnings.Add("network.reconnectAttempts out of range; clamped to 0-100.");
                config.Network.ReconnectAttempts = Math.Clamp(config.Network.ReconnectAttempts, 0, 100);
            }

            if (config.Network.ReconnectDelayMs < 0 || config.Network.ReconnectDelayMs > 60_000)
            {
                warnings.Add("network.reconnectDelayMs out of range; clamped to 0-60000.");
                config.Network.ReconnectDelayMs = Math.Clamp(config.Network.ReconnectDelayMs, 0, 60_000);
            }

            if (config.Performance.EncodingThreadCount < 0 || config.Performance.EncodingThreadCount > 64)
            {
                warnings.Add("performance.encodingThreadCount out of range; clamped to 0-64.");
                config.Performance.EncodingThreadCount = Math.Clamp(config.Performance.EncodingThreadCount, 0, 64);
            }

            // Hotkeys
            config.Hotkeys.ToggleMainWindow ??= new HotkeysConfig().ToggleMainWindow;
            config.Hotkeys.ConnectDisconnect ??= new HotkeysConfig().ConnectDisconnect;
            config.Hotkeys.NextDevice ??= new HotkeysConfig().NextDevice;
            config.Hotkeys.TogglePerformanceMode ??= new HotkeysConfig().TogglePerformanceMode;

            // Update
            if (!config.Update.Enabled)
            {
                // Keep manifest config as-is, but disable signature requirement when updates are off.
                config.Update.RequireManifestSignature = false;
            }

            if (config.Update.RequireManifestSignature && string.IsNullOrWhiteSpace(config.Update.TrustedManifestPublicKeyPem))
            {
                warnings.Add("update.requireManifestSignature enabled without trusted key; disabled.");
                config.Update.RequireManifestSignature = false;
            }

            if (!string.IsNullOrWhiteSpace(config.Update.ManifestUri))
            {
                string manifestValue = config.Update.ManifestUri.Trim();

                bool isAbsoluteUri = Uri.TryCreate(manifestValue, UriKind.Absolute, out _);
                bool isRootedPath = Path.IsPathRooted(manifestValue);

                if (!isAbsoluteUri && !isRootedPath)
                {
                    warnings.Add("update.manifestUri invalid; cleared (must be absolute URL or rooted file path).");
                    config.Update.ManifestUri = null;
                }
            }

            // Logging
            config.Logging.MinimumLevel ??= "Information";
            if (!IsValidLogLevel(config.Logging.MinimumLevel))
            {
                warnings.Add("logging.minimumLevel invalid; reset to Information.");
                config.Logging.MinimumLevel = "Information";
            }

            if (config.Logging.RetainedFileCountLimit < 1 || config.Logging.RetainedFileCountLimit > 365)
            {
                warnings.Add("logging.retainedFileCountLimit out of range; clamped to 1-365.");
                config.Logging.RetainedFileCountLimit = Math.Clamp(config.Logging.RetainedFileCountLimit, 1, 365);
            }

            if (config.Logging.RetentionDays < 1 || config.Logging.RetentionDays > 365)
            {
                warnings.Add("logging.retentionDays out of range; clamped to 1-365.");
                config.Logging.RetentionDays = Math.Clamp(config.Logging.RetentionDays, 1, 365);
            }

            if (config.Logging.FileSizeLimitMb < 1 || config.Logging.FileSizeLimitMb > 1024)
            {
                warnings.Add("logging.fileSizeLimitMb out of range; clamped to 1-1024.");
                config.Logging.FileSizeLimitMb = Math.Clamp(config.Logging.FileSizeLimitMb, 1, 1024);
            }

            return warnings;
        }

        private static bool IsValidLogLevel(string level)
        {
            return level.Equals("Verbose", StringComparison.OrdinalIgnoreCase)
                   || level.Equals("Debug", StringComparison.OrdinalIgnoreCase)
                   || level.Equals("Information", StringComparison.OrdinalIgnoreCase)
                   || level.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                   || level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                   || level.Equals("Fatal", StringComparison.OrdinalIgnoreCase);
        }

        private static AppConfig Clone(AppConfig config)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? AppConfig.CreateDefault();
        }

        private static bool StableJsonEquals(AppConfig a, AppConfig b)
        {
            string aj = JsonSerializer.Serialize(a, JsonOptions);
            string bj = JsonSerializer.Serialize(b, JsonOptions);
            return string.Equals(aj, bj, StringComparison.Ordinal);
        }

        private static string GetDefaultConfigPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(baseDir, "ExpandScreen", "config.json");
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            catch
            {
                // ignore best-effort backup
            }
        }

        public void Dispose()
        {
            StopWatching();
            _debounceTimer?.Dispose();
            _lock.Dispose();
        }
    }
}
