using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using ExpandScreen.Services.Update;
using ExpandScreen.UI.Services;
using Serilog;

namespace ExpandScreen.UI.ViewModels
{
    public sealed class UpdateViewModel : ViewModelBase
    {
        private string _statusTitle = "检查更新";
        private string _statusDetail = "尚未开始";
        private bool _isBusy;
        private bool _isUpdateAvailable;
        private string _latestVersionText = "-";
        private string _releaseNotes = string.Empty;
        private double _downloadProgressPercent;
        private UpdateInfo? _update;

        public UpdateViewModel()
        {
            CheckUpdatesCommand = new RelayCommand(async () => await CheckUpdatesAsync(), () => !IsBusy);
            DownloadAndInstallCommand = new RelayCommand(async () => await DownloadAndInstallAsync(), () => IsUpdateAvailable && !IsBusy);
        }

        public string CurrentVersionText => AppInfo.DisplayVersion;

        public string StatusTitle
        {
            get => _statusTitle;
            private set => SetProperty(ref _statusTitle, value);
        }

        public string StatusDetail
        {
            get => _statusDetail;
            private set => SetProperty(ref _statusDetail, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    ((RelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DownloadAndInstallCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            private set
            {
                if (SetProperty(ref _isUpdateAvailable, value))
                {
                    ((RelayCommand)DownloadAndInstallCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string LatestVersionText
        {
            get => _latestVersionText;
            private set => SetProperty(ref _latestVersionText, value);
        }

        public string ReleaseNotes
        {
            get => _releaseNotes;
            private set => SetProperty(ref _releaseNotes, value);
        }

        public double DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            private set => SetProperty(ref _downloadProgressPercent, value);
        }

        public ICommand CheckUpdatesCommand { get; }
        public ICommand DownloadAndInstallCommand { get; }

        private static Uri? GetManifestUriFromEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable("EXPANDSCREEN_UPDATE_MANIFEST");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return uri;
            }

            if (Path.IsPathRooted(value) && File.Exists(value))
            {
                return new Uri(value);
            }

            return null;
        }

        private async Task CheckUpdatesAsync()
        {
            IsBusy = true;
            IsUpdateAvailable = false;
            _update = null;
            DownloadProgressPercent = 0;
            LatestVersionText = "-";
            ReleaseNotes = string.Empty;

            try
            {
                Uri? manifestUri = GetManifestUriFromEnvironment();
                var service = new UpdateService(new UpdateServiceOptions(
                    ManifestUri: manifestUri,
                    CurrentVersion: AppInfo.CurrentVersion));

                if (manifestUri is null)
                {
                    StatusTitle = "未配置更新源";
                    StatusDetail = "设置环境变量 EXPANDSCREEN_UPDATE_MANIFEST 指向更新清单（URL 或本地文件）。";
                    return;
                }

                StatusTitle = "正在检查…";
                StatusDetail = manifestUri.IsFile ? $"读取清单: {manifestUri.LocalPath}" : $"请求清单: {manifestUri}";

                var result = await service.CheckForUpdatesAsync();
                if (!result.IsUpdateAvailable || result.Update is null)
                {
                    StatusTitle = "已是最新";
                    StatusDetail = $"当前版本 {AppInfo.DisplayVersion} 已是最新版本。";
                    return;
                }

                _update = result.Update;
                IsUpdateAvailable = true;
                LatestVersionText = _update.LatestVersion.ToString();
                ReleaseNotes = string.IsNullOrWhiteSpace(_update.ReleaseNotes) ? "（无发布说明）" : _update.ReleaseNotes!;

                StatusTitle = "发现新版本";
                StatusDetail = $"可更新至 {_update.LatestVersion}。";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Check for updates failed");
                StatusTitle = "检查失败";
                StatusDetail = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DownloadAndInstallAsync()
        {
            if (_update is null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                $"将下载并启动安装程序（版本 {_update.LatestVersion}）。\n\n安装启动后，应用将退出以完成更新。",
                "下载并安装更新",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            IsBusy = true;
            DownloadProgressPercent = 0;

            try
            {
                Uri? manifestUri = GetManifestUriFromEnvironment();
                var service = new UpdateService(new UpdateServiceOptions(
                    ManifestUri: manifestUri,
                    CurrentVersion: AppInfo.CurrentVersion));

                StatusTitle = "正在下载…";
                StatusDetail = $"来源: {_update.DownloadUri}";

                string destinationDirectory = Path.Combine(Path.GetTempPath(), "ExpandScreen", "updates", _update.LatestVersion.ToString());
                var progress = new Progress<double>(p =>
                {
                    DownloadProgressPercent = Math.Clamp(p * 100.0, 0.0, 100.0);
                });

                var downloaded = await service.DownloadUpdateAsync(_update, destinationDirectory, progress);

                StatusTitle = "下载完成";
                StatusDetail = downloaded.FilePath;

                StatusTitle = "启动安装程序…";
                var startInfo = new ProcessStartInfo(downloaded.FilePath)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Download/apply update failed");
                StatusTitle = "更新失败";
                StatusDetail = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
