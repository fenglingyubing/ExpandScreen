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
        private readonly PerformanceMonitor _monitor = new();
        private readonly DispatcherTimer _timer;

        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _memHistory = new();
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
            StartRecordingCommand = new RelayCommand(_ => ExecuteStartRecording(), _ => !IsRecording);
            StopRecordingCommand = new RelayCommand(_ => ExecuteStopRecording(), _ => IsRecording);
            ExportJsonCommand = new RelayCommand(_ => ExecuteExportJson(), _ => _lastRun != null);
            ExportCsvCommand = new RelayCommand(_ => ExecuteExportCsv(), _ => _lastRun != null);
            ClearCommand = new RelayCommand(_ => ExecuteClear());

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
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

        private void Tick()
        {
            var snap = _monitor.GetSnapshot();

            CpuNowText = $"{snap.CpuUsagePercent:F1}%";
            MemNowText = $"{snap.WorkingSetMb:F0} MB";
            HeapNowText = $"{snap.ManagedHeapMb:F0} MB";
            RttNowText = snap.LastHeartbeatRttMs.HasValue
                ? $"{snap.LastHeartbeatRttMs.Value:F1} ms / avg {snap.AverageHeartbeatRttMs.GetValueOrDefault():F1} ms"
                : "N/A";
            FpsNowText = snap.CurrentFps.HasValue ? $"{snap.CurrentFps.Value:F1} fps" : "N/A";
            LatencyNowText = snap.CurrentLatencyMs.HasValue ? $"{snap.CurrentLatencyMs.Value:F1} ms" : "N/A";

            PushHistory(_cpuHistory, snap.CpuUsagePercent, 90);
            PushHistory(_memHistory, snap.WorkingSetMb, 90);
            CpuSparkline = BuildSparkline(_cpuHistory, max: 100, width: 420, height: 80);
            MemSparkline = BuildSparkline(_memHistory, max: Math.Max(1, _memHistory.Max()), width: 420, height: 80);

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
                var dialog = new SaveFileDialog
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
                var dialog = new SaveFileDialog
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
        }

        private static void PushHistory(List<double> list, double value, int max)
        {
            list.Add(value);
            while (list.Count > max)
            {
                list.RemoveAt(0);
            }
        }

        private static PointCollection BuildSparkline(IReadOnlyList<double> values, double max, double width, double height)
        {
            var points = new PointCollection();
            if (values.Count == 0)
            {
                return points;
            }

            max = Math.Max(1, max);
            double stepX = values.Count == 1 ? 0 : width / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * stepX;
                double v = Math.Clamp(values[i] / max, 0, 1);
                double y = (1 - v) * height;
                points.Add(new Point(x, y));
            }

            return points;
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
                _timer.Stop();
            }
            catch
            {
            }
        }
    }
}

