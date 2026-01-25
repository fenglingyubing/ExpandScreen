using System.Text.Json;
using ExpandScreen.Services.Diagnostics;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Analytics
{
    public sealed class AnalyticsService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly Func<DateTime> _nowUtc;
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly object _lock = new();
        private readonly string _storePath;

        private AnalyticsOptions _options = new();
        private AnalyticsStore _store = new();
        private bool _initialized;
        private bool _disposed;

        private DateTime? _appStartUtc;
        private readonly Dictionary<string, DateTime> _activeConnectionsByDeviceHash = new(StringComparer.OrdinalIgnoreCase);
        private readonly PerformanceMonitor _performanceMonitor = new();

        private Timer? _sampleTimer;
        private Timer? _flushTimer;
        private volatile int _dirty;

        public AnalyticsService(string? storePath = null, Func<DateTime>? nowUtc = null)
        {
            _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
            _storePath = string.IsNullOrWhiteSpace(storePath)
                ? Path.Combine(AppPaths.GetAnalyticsDirectory(), "analytics.json")
                : storePath!;
        }

        public event EventHandler? Updated;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            _store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }

        public void ApplyOptions(AnalyticsOptions options)
        {
            ThrowIfDisposed();
            if (options == null) throw new ArgumentNullException(nameof(options));

            bool wasEnabled;
            lock (_lock)
            {
                wasEnabled = _options.Enabled;
                _options = options;

                if (wasEnabled && !options.Enabled)
                {
                    FinalizeDurationsLocked();
                    MarkDirtyLocked();
                }

                if (!wasEnabled && options.Enabled)
                {
                    _appStartUtc ??= _nowUtc();
                }
            }

            RefreshTimers();
        }

        public void TrackAppStarted()
        {
            ThrowIfDisposed();
            if (!_initialized) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    _appStartUtc = null;
                    return;
                }

                _store.LaunchCount++;
                _appStartUtc = _nowUtc();
                MarkDirtyLocked();
                PruneLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public void TrackAppStopped()
        {
            ThrowIfDisposed();
            if (!_initialized) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    _appStartUtc = null;
                    _activeConnectionsByDeviceHash.Clear();
                    return;
                }

                FinalizeDurationsLocked();
                MarkDirtyLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public void TrackFeatureUsed(string featureName)
        {
            ThrowIfDisposed();
            if (!_initialized) return;
            if (string.IsNullOrWhiteSpace(featureName)) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    return;
                }

                _store.FeatureUsage.TryGetValue(featureName, out int existing);
                _store.FeatureUsage[featureName] = existing + 1;
                _store.History.Add(new AnalyticsEvent
                {
                    TimestampUtc = _nowUtc(),
                    Type = "feature",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = featureName
                    }
                });

                MarkDirtyLocked();
                PruneLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public void TrackConnected(string deviceId)
        {
            ThrowIfDisposed();
            if (!_initialized) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    return;
                }

                string deviceHash = AnalyticsAnonymizer.HashDeviceId(_store.InstallId, deviceId);
                _store.TotalConnections++;

                _activeConnectionsByDeviceHash[deviceHash] = _nowUtc();

                _store.History.Add(new AnalyticsEvent
                {
                    TimestampUtc = _nowUtc(),
                    Type = "connect",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["device"] = deviceHash
                    }
                });

                MarkDirtyLocked();
                PruneLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public void TrackDisconnected(string deviceId)
        {
            ThrowIfDisposed();
            if (!_initialized) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    return;
                }

                string deviceHash = AnalyticsAnonymizer.HashDeviceId(_store.InstallId, deviceId);
                if (_activeConnectionsByDeviceHash.TryGetValue(deviceHash, out var startedUtc))
                {
                    var elapsed = _nowUtc() - startedUtc;
                    if (elapsed.TotalSeconds > 0)
                    {
                        _store.TotalConnectedSeconds += elapsed.TotalSeconds;
                    }

                    _activeConnectionsByDeviceHash.Remove(deviceHash);
                }

                _store.History.Add(new AnalyticsEvent
                {
                    TimestampUtc = _nowUtc(),
                    Type = "disconnect",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["device"] = deviceHash
                    }
                });

                MarkDirtyLocked();
                PruneLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public AnalyticsSnapshot GetSnapshot()
        {
            ThrowIfDisposed();
            if (!_initialized)
            {
                return new AnalyticsSnapshot { Enabled = false };
            }

            lock (_lock)
            {
                return new AnalyticsSnapshot
                {
                    Enabled = _options.Enabled,
                    GeneratedUtc = _nowUtc(),
                    LaunchCount = _store.LaunchCount,
                    TotalAppTime = TimeSpan.FromSeconds(Math.Max(0, _store.TotalAppSeconds)),
                    TotalConnections = _store.TotalConnections,
                    TotalConnectedTime = TimeSpan.FromSeconds(Math.Max(0, _store.TotalConnectedSeconds)),
                    FeatureUsage = new Dictionary<string, int>(_store.FeatureUsage, StringComparer.OrdinalIgnoreCase),
                    History = _store.History.ToArray(),
                    PerformanceSamples = _store.PerformanceSamples.ToArray()
                };
            }
        }

        public async Task<string> ExportReportAsync(string? outputDirectory = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return await AnalyticsExportService.ExportAsync(GetSnapshot(), outputDirectory, cancellationToken).ConfigureAwait(false);
        }

        public async Task ClearDataAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                _store = new AnalyticsStore();
                _activeConnectionsByDeviceHash.Clear();
                _appStartUtc = null;
                MarkDirtyLocked();
            }

            await FlushAsync(cancellationToken).ConfigureAwait(false);
            Updated?.Invoke(this, EventArgs.Empty);
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!_initialized)
            {
                return;
            }

            if (Interlocked.Exchange(ref _dirty, 0) == 0)
            {
                return;
            }

            AnalyticsStore snapshot;
            lock (_lock)
            {
                snapshot = CloneStoreLocked();
            }

            await SaveStoreAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        private void RefreshTimers()
        {
            if (!_initialized)
            {
                return;
            }

            bool enabled;
            int sampleIntervalSec;
            lock (_lock)
            {
                enabled = _options.Enabled;
                sampleIntervalSec = _options.PerformanceSampleIntervalSeconds;
            }

            if (!enabled)
            {
                _sampleTimer?.Dispose();
                _sampleTimer = null;

                _flushTimer?.Dispose();
                _flushTimer = null;
                return;
            }

            if (sampleIntervalSec <= 0)
            {
                _sampleTimer?.Dispose();
                _sampleTimer = null;
            }
            else if (_sampleTimer == null)
            {
                _sampleTimer = new Timer(_ => SafeSamplePerformance(), null, TimeSpan.FromSeconds(sampleIntervalSec), TimeSpan.FromSeconds(sampleIntervalSec));
            }
            else
            {
                _sampleTimer.Change(TimeSpan.FromSeconds(sampleIntervalSec), TimeSpan.FromSeconds(sampleIntervalSec));
            }

            _flushTimer ??= new Timer(_ => SafeFlush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private void SafeSamplePerformance()
        {
            try
            {
                SamplePerformance();
            }
            catch
            {
            }
        }

        private void SamplePerformance()
        {
            if (!_initialized) return;

            lock (_lock)
            {
                if (!_options.Enabled)
                {
                    return;
                }

                var snap = _performanceMonitor.GetSnapshot();
                _store.PerformanceSamples.Add(new AnalyticsPerformanceSample
                {
                    TimestampUtc = _nowUtc(),
                    CpuUsagePercent = snap.CpuUsagePercent,
                    WorkingSetMb = snap.WorkingSetMb,
                    ManagedHeapMb = snap.ManagedHeapMb
                });

                MarkDirtyLocked();
                PruneLocked();
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        private void SafeFlush()
        {
            try
            {
                _ = FlushAsync();
            }
            catch
            {
            }
        }

        private void MarkDirtyLocked()
        {
            Interlocked.Exchange(ref _dirty, 1);
        }

        private void PruneLocked()
        {
            int maxHistory = Math.Max(0, _options.MaxHistoryEntries);
            if (maxHistory > 0 && _store.History.Count > maxHistory)
            {
                int remove = _store.History.Count - maxHistory;
                _store.History.RemoveRange(0, remove);
            }

            int maxSamples = Math.Max(0, _options.MaxPerformanceSamples);
            if (maxSamples > 0 && _store.PerformanceSamples.Count > maxSamples)
            {
                int remove = _store.PerformanceSamples.Count - maxSamples;
                _store.PerformanceSamples.RemoveRange(0, remove);
            }
        }

        private void FinalizeDurationsLocked()
        {
            var now = _nowUtc();

            if (_appStartUtc.HasValue)
            {
                var elapsed = now - _appStartUtc.Value;
                if (elapsed.TotalSeconds > 0)
                {
                    _store.TotalAppSeconds += elapsed.TotalSeconds;
                }
            }

            foreach (var startedUtc in _activeConnectionsByDeviceHash.Values)
            {
                var elapsed = now - startedUtc;
                if (elapsed.TotalSeconds > 0)
                {
                    _store.TotalConnectedSeconds += elapsed.TotalSeconds;
                }
            }

            _activeConnectionsByDeviceHash.Clear();
            _appStartUtc = null;
        }

        private AnalyticsStore CloneStoreLocked()
        {
            string json = JsonSerializer.Serialize(_store, JsonOptions);
            return JsonSerializer.Deserialize<AnalyticsStore>(json, JsonOptions) ?? new AnalyticsStore();
        }

        private async Task<AnalyticsStore> LoadStoreAsync(CancellationToken cancellationToken)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_storePath))
                {
                    return new AnalyticsStore();
                }

                string text = await File.ReadAllTextAsync(_storePath, cancellationToken).ConfigureAwait(false);
                AnalyticsStore? parsed = null;
                try
                {
                    parsed = JsonSerializer.Deserialize<AnalyticsStore>(text, JsonOptions);
                }
                catch
                {
                }

                if (parsed == null)
                {
                    string badPath = $"{_storePath}.bad-{DateTime.UtcNow:yyyyMMddHHmmss}";
                    TryMove(_storePath, badPath);
                    return new AnalyticsStore();
                }

                parsed.FeatureUsage ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                parsed.History ??= new List<AnalyticsEvent>();
                parsed.PerformanceSamples ??= new List<AnalyticsPerformanceSample>();
                if (string.IsNullOrWhiteSpace(parsed.InstallId))
                {
                    parsed.InstallId = Guid.NewGuid().ToString("N");
                }

                return parsed;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task SaveStoreAsync(AnalyticsStore store, CancellationToken cancellationToken)
        {
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
                string json = JsonSerializer.Serialize(store, JsonOptions);
                await File.WriteAllTextAsync(_storePath, json, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileLock.Release();
            }
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
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AnalyticsService));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                TrackAppStopped();
            }
            catch
            {
            }

            try
            {
                _sampleTimer?.Dispose();
                _flushTimer?.Dispose();
            }
            catch
            {
            }

            try
            {
                _fileLock.Dispose();
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}
