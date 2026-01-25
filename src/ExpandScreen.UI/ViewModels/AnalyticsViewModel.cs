using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using ExpandScreen.Services.Analytics;
using ExpandScreen.Services.Configuration;

namespace ExpandScreen.UI.ViewModels
{
    public sealed class AnalyticsEventRow
    {
        public string TimeText { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
    }

    public sealed class AnalyticsFeatureRow
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Ratio { get; set; }
    }

    public sealed class AnalyticsViewModel : ViewModelBase, IDisposable
    {
        private readonly AnalyticsService _analyticsService;
        private readonly ConfigService _configService;

        private bool _consentPromptVisible;
        private bool _isEnabled;
        private string _statusText = string.Empty;
        private string _totalAppTimeText = "—";
        private string _totalConnectedTimeText = "—";
        private string _launchCountText = "—";
        private string _totalConnectionsText = "—";
        private string _cpuNowText = "—";
        private string _memNowText = "—";
        private PointCollection _cpuSparkline = new();
        private PointCollection _memSparkline = new();

        public AnalyticsViewModel(AnalyticsService analyticsService, ConfigService configService)
        {
            _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            EnableAnalyticsCommand = new RelayCommand(_ => ExecuteEnableAnalytics(), _ => ConsentPromptVisible);
            DeclineAnalyticsCommand = new RelayCommand(_ => ExecuteDeclineAnalytics(), _ => ConsentPromptVisible);
            DisableAnalyticsCommand = new RelayCommand(_ => ExecuteDisableAnalytics(), _ => IsEnabled);
            ExportReportCommand = new RelayCommand(_ => ExecuteExportReport());
            ClearDataCommand = new RelayCommand(_ => ExecuteClearData());

            _analyticsService.Updated += OnAnalyticsUpdated;
            _configService.ConfigChanged += OnConfigChanged;

            RefreshFromConfig(_configService.GetSnapshot());
            RefreshFromSnapshot(_analyticsService.GetSnapshot());
        }

        public ObservableCollection<AnalyticsFeatureRow> TopFeatures { get; } = new();
        public ObservableCollection<AnalyticsEventRow> RecentEvents { get; } = new();

        public bool ConsentPromptVisible
        {
            get => _consentPromptVisible;
            private set
            {
                if (SetProperty(ref _consentPromptVisible, value))
                {
                    EnableAnalyticsCommand.RaiseCanExecuteChanged();
                    DeclineAnalyticsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    DisableAnalyticsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string TotalAppTimeText
        {
            get => _totalAppTimeText;
            private set => SetProperty(ref _totalAppTimeText, value);
        }

        public string TotalConnectedTimeText
        {
            get => _totalConnectedTimeText;
            private set => SetProperty(ref _totalConnectedTimeText, value);
        }

        public string LaunchCountText
        {
            get => _launchCountText;
            private set => SetProperty(ref _launchCountText, value);
        }

        public string TotalConnectionsText
        {
            get => _totalConnectionsText;
            private set => SetProperty(ref _totalConnectionsText, value);
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

        public RelayCommand EnableAnalyticsCommand { get; }
        public RelayCommand DeclineAnalyticsCommand { get; }
        public RelayCommand DisableAnalyticsCommand { get; }
        public RelayCommand ExportReportCommand { get; }
        public RelayCommand ClearDataCommand { get; }

        private void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RefreshFromConfig(e.Config);
                RefreshFromSnapshot(_analyticsService.GetSnapshot());
            });
        }

        private void OnAnalyticsUpdated(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.InvokeAsync(() => RefreshFromSnapshot(_analyticsService.GetSnapshot()));
        }

        private void RefreshFromConfig(AppConfig config)
        {
            ConsentPromptVisible = !config.Analytics.ConsentPrompted;
            IsEnabled = config.Analytics.Enabled;
            StatusText = config.Analytics.Enabled
                ? "已启用：仅本机保存，不上传"
                : "未启用：不会记录或保存数据";
        }

        private void RefreshFromSnapshot(AnalyticsSnapshot snapshot)
        {
            LaunchCountText = snapshot.LaunchCount.ToString();
            TotalAppTimeText = FormatDuration(snapshot.TotalAppTime);
            TotalConnectionsText = snapshot.TotalConnections.ToString();
            TotalConnectedTimeText = FormatDuration(snapshot.TotalConnectedTime);

            var latest = snapshot.PerformanceSamples.LastOrDefault();
            if (latest != null)
            {
                CpuNowText = $"{latest.CpuUsagePercent:F1}%";
                MemNowText = $"{latest.WorkingSetMb:F0} MB";
            }
            else
            {
                CpuNowText = "—";
                MemNowText = "—";
            }

            UpdateTopFeatures(snapshot);
            UpdateRecentEvents(snapshot);
            UpdateSparklines(snapshot);
        }

        private void UpdateTopFeatures(AnalyticsSnapshot snapshot)
        {
            TopFeatures.Clear();
            if (!snapshot.Enabled || snapshot.FeatureUsage.Count == 0)
            {
                return;
            }

            int max = snapshot.FeatureUsage.Values.DefaultIfEmpty(0).Max();
            if (max <= 0)
            {
                return;
            }

            foreach (var item in snapshot.FeatureUsage
                         .OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(8))
            {
                TopFeatures.Add(new AnalyticsFeatureRow
                {
                    Name = item.Key,
                    Count = item.Value,
                    Ratio = Math.Clamp(item.Value / (double)max, 0, 1)
                });
            }
        }

        private void UpdateRecentEvents(AnalyticsSnapshot snapshot)
        {
            RecentEvents.Clear();
            if (!snapshot.Enabled || snapshot.History.Count == 0)
            {
                return;
            }

            foreach (var ev in snapshot.History
                         .OrderByDescending(x => x.TimestampUtc)
                         .Take(40))
            {
                RecentEvents.Add(new AnalyticsEventRow
                {
                    TimeText = ev.TimestampUtc.ToLocalTime().ToString("MM-dd HH:mm:ss"),
                    TypeText = ev.Type,
                    DetailText = FormatEventDetail(ev)
                });
            }
        }

        private void UpdateSparklines(AnalyticsSnapshot snapshot)
        {
            var samples = snapshot.PerformanceSamples.TakeLast(60).ToArray();
            if (samples.Length == 0)
            {
                CpuSparkline = new PointCollection();
                MemSparkline = new PointCollection();
                return;
            }

            var cpuValues = samples.Select(s => s.CpuUsagePercent).ToArray();
            var memValues = samples.Select(s => s.WorkingSetMb).ToArray();
            double memMax = Math.Max(1, memValues.Max());

            CpuSparkline = BuildSparkline(cpuValues, max: 100, width: 420, height: 80);
            MemSparkline = BuildSparkline(memValues, max: memMax, width: 420, height: 80);
        }

        private static PointCollection BuildSparkline(IReadOnlyList<double> values, double max, double width, double height)
        {
            var points = new PointCollection();
            if (values.Count == 0 || max <= 0 || width <= 0 || height <= 0)
            {
                return points;
            }

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

        private static string FormatEventDetail(AnalyticsEvent ev)
        {
            if (ev.Data == null || ev.Data.Count == 0)
            {
                return string.Empty;
            }

            if (ev.Type.Equals("feature", StringComparison.OrdinalIgnoreCase)
                && ev.Data.TryGetValue("name", out var name))
            {
                return name;
            }

            if ((ev.Type.Equals("connect", StringComparison.OrdinalIgnoreCase)
                 || ev.Type.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
                && ev.Data.TryGetValue("device", out var device))
            {
                return $"device:{device}";
            }

            return string.Join(", ", ev.Data.Select(kv => $"{kv.Key}:{kv.Value}"));
        }

        private static string FormatDuration(TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
            {
                return "0s";
            }

            if (time.TotalMinutes < 1)
            {
                return $"{(int)time.TotalSeconds}s";
            }

            if (time.TotalHours < 1)
            {
                return $"{(int)time.TotalMinutes}m {time.Seconds:D2}s";
            }

            if (time.TotalDays < 1)
            {
                return $"{(int)time.TotalHours}h {time.Minutes:D2}m";
            }

            return $"{(int)time.TotalDays}d {time.Hours:D2}h";
        }

        private async void ExecuteEnableAnalytics()
        {
            try
            {
                var config = _configService.GetSnapshot();
                config.Analytics.Enabled = true;
                config.Analytics.ConsentPrompted = true;
                await _configService.SaveAsync(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启用失败：{ex.Message}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteDeclineAnalytics()
        {
            try
            {
                var config = _configService.GetSnapshot();
                config.Analytics.Enabled = false;
                config.Analytics.ConsentPrompted = true;
                await _configService.SaveAsync(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteDisableAnalytics()
        {
            try
            {
                var config = _configService.GetSnapshot();
                config.Analytics.Enabled = false;
                config.Analytics.ConsentPrompted = true;
                await _configService.SaveAsync(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停用失败：{ex.Message}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteExportReport()
        {
            try
            {
                string path = await _analyticsService.ExportReportAsync();
                MessageBox.Show($"已导出：\n{path}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExecuteClearData()
        {
            var result = MessageBox.Show(
                "确定要清除本机统计数据吗？",
                "清除统计数据",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _analyticsService.ClearDataAsync();
                MessageBox.Show("已清除。", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除失败：{ex.Message}", "统计与分析", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            _analyticsService.Updated -= OnAnalyticsUpdated;
            _configService.ConfigChanged -= OnConfigChanged;
        }
    }
}

