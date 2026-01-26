using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ExpandScreen.Services.Diagnostics;
using ExpandScreen.Utils;
using Microsoft.Win32;

namespace ExpandScreen.UI.ViewModels
{
    public sealed class PerformanceTestViewModel : ViewModelBase, IDisposable
    {
        private const int SampleIntervalMs = 500;
        private const int HistorySize = 90;
        private const double SparklineWidth = 420;
        private const double SparklineHeight = 80;

        private readonly PerformanceMonitor _monitor = new();
        private readonly Dispatcher _uiDispatcher;
        private readonly CancellationTokenSource _samplingCts = new();
        private readonly Task _samplingTask;

        private readonly double[] _cpuHistory = new double[HistorySize];
        private int _cpuHistoryHead;
        private int _cpuHistoryCount;

        private readonly double[] _memHistory = new double[HistorySize];
        private int _memHistoryHead;
        private int _memHistoryCount;

        private readonly List<PerformanceSnapshot> _recordedSamples = new();

        private bool _isRecording;
        private DateTime? _recordingStartedUtc;
        private PerformanceSampleSeries? _lastRun;

        private string _statusText = "就绪";
        private string _cpuNowText = "—";
        private string _memNowText = "—";
        private string _heapNowText = "—";
        private string _rttNowText = "—";
        private string _fpsNowText = "—";
        private string _latencyNowText = "—";
        private string _runSummaryText = "尚无记录";

        private PointCollection _cpuSparkline = new();
        private PointCollection _memSparkline = new();

        public PerformanceTestViewModel()
        {
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            StartRecordingCommand = new RelayCommand(_ => ExecuteStartRecording(), _ => !IsRecording);
            StopRecordingCommand = new RelayCommand(_ => ExecuteStopRecording(), _ => IsRecording);
            ExportJsonCommand = new RelayCommand(_ => ExecuteExportJson(), _ => _lastRun != null);
            ExportCsvCommand = new RelayCommand(_ => ExecuteExportCsv(), _ => _lastRun != null);
            ClearCommand = new RelayCommand(_ => ExecuteClear());

            CpuSparkline = new PointCollection();
            MemSparkline = new PointCollection();

            _samplingTask = Task.Run(() => SamplingLoopAsync(_samplingCts.Token));
        }

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    StartRecordingCommand.RaiseCanExecuteChanged();
                    StopRecordingCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string CpuNowText
        {
            get => _cpuNowText;
            private set => SetProperty(ref _cpuNowText, value);
        }

        public string MemNowText
        {
            get => _memNowText;
            private set => SetProperty(ref _memNowText, value);
        }

        public string HeapNowText
        {
            get => _heapNowText;
            private set => SetProperty(ref _heapNowText, value);
        }

        public string RttNowText
        {
            get => _rttNowText;
            private set => SetProperty(ref _rttNowText, value);
        }

        public string FpsNowText
        {
            get => _fpsNowText;
            private set => SetProperty(ref _fpsNowText, value);
        }

        public string LatencyNowText
        {
            get => _latencyNowText;
            private set => SetProperty(ref _latencyNowText, value);
        }

        public string RunSummaryText
        {
            get => _runSummaryText;
            private set => SetProperty(ref _runSummaryText, value);
        }

        public PointCollection CpuSparkline
        {
            get => _cpuSparkline;
            private set => SetProperty(ref _cpuSparkline, value);
        }

        public PointCollection MemSparkline
        {
            get => _memSparkline;
            private set => SetProperty(ref _memSparkline, value);
        }

        public RelayCommand StartRecordingCommand { get; }
        public RelayCommand StopRecordingCommand { get; }
        public RelayCommand ExportJsonCommand { get; }
        public RelayCommand ExportCsvCommand { get; }
        public RelayCommand ClearCommand { get; }

        private async Task SamplingLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SampleIntervalMs));

            while (!cancellationToken.IsCancellationRequested)
            {
                bool ticked;
                try
                {
                    ticked = await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!ticked)
                {
                    break;
                }

                PerformanceSnapshot snap;
                try
                {
                    snap = _monitor.GetSnapshot();
                }
                catch
                {
                    continue;
                }

                try
                {
                    await _uiDispatcher.InvokeAsync(
                            () => ApplySnapshot(snap),
                            DispatcherPriority.Background)
                        .Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // ignore (window may be closing)
                }
            }
        }

        private void ExecuteStartRecording()
        {
            _recordedSamples.Clear();
            _recordingStartedUtc = DateTime.UtcNow;
            IsRecording = true;
        }

        private void ExecuteStopRecording()
        {
            IsRecording = false;

            var end = DateTime.UtcNow;
            var series = new PerformanceSampleSeries
            {
                StartedUtc = _recordingStartedUtc ?? end,
                EndedUtc = end,
                Samples = _recordedSamples.ToList()
            };

            _lastRun = series;
            RunSummaryText = series.BuildQuickSummary();

            ExportJsonCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
        }

        private async void ExecuteExportJson()
        {
            if (_lastRun == null)
            {
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出性能采样（JSON）",
                    Filter = "JSON (*.json)|*.json",
                    FileName = $"expandscreen-perf-{DateTime.UtcNow:yyyyMMddHHmmss}.json",
                    InitialDirectory = EnsureDir(AppPaths.GetDiagnosticsDirectory())
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                await _lastRun.ExportJsonAsync(dialog.FileName);
                MessageBox.Show($"已导出：\n{dialog.FileName}", "性能测试", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "性能测试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteExportCsv()
        {
            if (_lastRun == null)
            {
                return;
            }

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出性能采样（CSV）",
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = $"expandscreen-perf-{DateTime.UtcNow:yyyyMMddHHmmss}.csv",
                    InitialDirectory = EnsureDir(AppPaths.GetDiagnosticsDirectory())
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                await _lastRun.ExportCsvAsync(dialog.FileName);
                MessageBox.Show($"已导出：\n{dialog.FileName}", "性能测试", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "性能测试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteClear()
        {
            _lastRun = null;
            RunSummaryText = "尚无记录";
            _recordedSamples.Clear();
            _recordingStartedUtc = null;
            IsRecording = false;
            ExportJsonCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();

            Array.Clear(_cpuHistory, 0, _cpuHistory.Length);
            _cpuHistoryHead = 0;
            _cpuHistoryCount = 0;
            Array.Clear(_memHistory, 0, _memHistory.Length);
            _memHistoryHead = 0;
            _memHistoryCount = 0;
            CpuSparkline.Clear();
            MemSparkline.Clear();
        }

        private void ApplySnapshot(PerformanceSnapshot snap)
        {
            CpuNowText = $"{snap.CpuUsagePercent:F1}%";
            MemNowText = $"{snap.WorkingSetMb:F0} MB";
            HeapNowText = $"{snap.ManagedHeapMb:F0} MB";
            RttNowText = snap.LastHeartbeatRttMs.HasValue
                ? $"{snap.LastHeartbeatRttMs.Value:F1} ms / avg {snap.AverageHeartbeatRttMs.GetValueOrDefault():F1} ms"
                : "N/A";
            FpsNowText = snap.CurrentFps.HasValue ? $"{snap.CurrentFps.Value:F1} fps" : "N/A";
            LatencyNowText = snap.CurrentLatencyMs.HasValue ? $"{snap.CurrentLatencyMs.Value:F1} ms" : "N/A";

            PushHistory(_cpuHistory, ref _cpuHistoryHead, ref _cpuHistoryCount, snap.CpuUsagePercent);
            PushHistory(_memHistory, ref _memHistoryHead, ref _memHistoryCount, snap.WorkingSetMb);

            UpdateSparkline(CpuSparkline, _cpuHistory, _cpuHistoryHead, _cpuHistoryCount, max: 100, width: SparklineWidth, height: SparklineHeight);
            UpdateSparkline(MemSparkline, _memHistory, _memHistoryHead, _memHistoryCount, max: Math.Max(1, GetHistoryMax(_memHistory, _memHistoryHead, _memHistoryCount)), width: SparklineWidth, height: SparklineHeight);

            if (IsRecording)
            {
                _recordedSamples.Add(snap);
                var elapsed = _recordingStartedUtc.HasValue ? (DateTime.UtcNow - _recordingStartedUtc.Value) : TimeSpan.Zero;
                StatusText = $"录制中… {elapsed.TotalSeconds:F1}s（{_recordedSamples.Count} samples）";
            }
            else
            {
                StatusText = "就绪";
            }
        }

        private static void PushHistory(double[] buffer, ref int head, ref int count, double value)
        {
            buffer[head] = value;
            head = (head + 1) % buffer.Length;
            if (count < buffer.Length)
            {
                count++;
            }
        }

        private static double GetHistoryMax(double[] buffer, int head, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            double max = double.MinValue;
            int start = count == buffer.Length ? head : 0;
            for (int i = 0; i < count; i++)
            {
                double v = buffer[(start + i) % buffer.Length];
                max = Math.Max(max, v);
            }
            return max;
        }

        private static void UpdateSparkline(PointCollection points, double[] buffer, int head, int count, double max, double width, double height)
        {
            points.Clear();
            if (count <= 0)
            {
                return;
            }

            max = Math.Max(1, max);
            double stepX = count == 1 ? 0 : width / (count - 1);

            int start = count == buffer.Length ? head : 0;
            for (int i = 0; i < count; i++)
            {
                double x = i * stepX;
                double v = Math.Clamp(buffer[(start + i) % buffer.Length] / max, 0, 1);
                double y = (1 - v) * height;
                points.Add(new System.Windows.Point(x, y));
            }
        }

        private static string EnsureDir(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch
            {
            }
            return path;
        }

        public void Dispose()
        {
            try
            {
                _samplingCts.Cancel();
            }
            catch
            {
            }

            try
            {
                _samplingCts.Dispose();
            }
            catch
            {
            }
        }
    }
}
