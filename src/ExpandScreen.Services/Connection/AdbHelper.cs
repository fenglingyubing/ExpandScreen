using System.Diagnostics;
using System.Text;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// ADB工具封装类
    /// </summary>
    public class AdbHelper
    {
        private readonly string _adbPath;
        private const int DefaultTimeoutMs = 5000;

        public AdbHelper(string? adbPath = null)
        {
            // 默认在当前目录的adb子目录或系统PATH中查找
            _adbPath = adbPath ?? FindAdbExecutable();

            if (!File.Exists(_adbPath))
            {
                throw new FileNotFoundException($"ADB executable not found at: {_adbPath}");
            }

            LogHelper.Info($"ADB Helper initialized with path: {_adbPath}");
        }

        /// <summary>
        /// 查找ADB可执行文件
        /// </summary>
        private string FindAdbExecutable()
        {
            // 检查当前目录的adb子目录
            string localAdb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb", "adb.exe");
            if (File.Exists(localAdb))
            {
                return localAdb;
            }

            // 检查PATH环境变量
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                foreach (string path in pathEnv.Split(';'))
                {
                    string adbPath = Path.Combine(path.Trim(), "adb.exe");
                    if (File.Exists(adbPath))
                    {
                        return adbPath;
                    }
                }
            }

            // 默认路径
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb", "adb.exe");
        }

        /// <summary>
        /// 执行ADB命令
        /// </summary>
        public async Task<(bool success, string output, string error)> ExecuteCommandAsync(
            string arguments,
            int timeoutMs = DefaultTimeoutMs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                LogHelper.Debug($"Executing ADB command: adb {arguments}");

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        LogHelper.Warning($"ADB command timed out after {timeoutMs}ms: adb {arguments}");
                        return (false, "", "Command timed out");
                    }
                }

                string output = outputBuilder.ToString().Trim();
                string error = errorBuilder.ToString().Trim();

                bool success = process.ExitCode == 0;

                if (!success)
                {
                    LogHelper.Warning($"ADB command failed (exit code {process.ExitCode}): adb {arguments}\nError: {error}");
                }
                else
                {
                    LogHelper.Debug($"ADB command succeeded: adb {arguments}");
                }

                return (success, output, error);
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error executing ADB command: adb {arguments}", ex);
                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// 获取连接的设备列表
        /// </summary>
        public async Task<List<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<AndroidDevice>();

            var (success, output, _) = await ExecuteCommandAsync("devices -l", cancellationToken: cancellationToken);

            if (!success || string.IsNullOrEmpty(output))
            {
                return devices;
            }

            // 解析输出
            // 格式: device_id    device product:xxx model:xxx device:xxx
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices") || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var device = new AndroidDevice
                {
                    DeviceId = parts[0],
                    Status = parts[1]
                };

                // 解析其他属性
                foreach (var part in parts.Skip(2))
                {
                    if (part.StartsWith("model:"))
                    {
                        device.Model = part.Substring(6);
                    }
                    else if (part.StartsWith("device:"))
                    {
                        device.DeviceName = part.Substring(7);
                    }
                }

                devices.Add(device);
            }

            return devices;
        }

        /// <summary>
        /// 获取设备详细信息
        /// </summary>
        public async Task<AndroidDevice?> GetDeviceInfoAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var device = new AndroidDevice
            {
                DeviceId = deviceId,
                Status = "device"
            };

            // 获取设备属性
            var props = new Dictionary<string, string>
            {
                { "ro.product.model", nameof(AndroidDevice.Model) },
                { "ro.product.manufacturer", nameof(AndroidDevice.Manufacturer) },
                { "ro.build.version.release", nameof(AndroidDevice.AndroidVersion) },
                { "ro.build.version.sdk", "SdkVersion" }
            };

            foreach (var (prop, field) in props)
            {
                var (success, output, _) = await ExecuteCommandAsync(
                    $"-s {deviceId} shell getprop {prop}",
                    cancellationToken: cancellationToken);

                if (success && !string.IsNullOrEmpty(output))
                {
                    string value = output.Trim();

                    switch (field)
                    {
                        case nameof(AndroidDevice.Model):
                            device.Model = value;
                            break;
                        case nameof(AndroidDevice.Manufacturer):
                            device.Manufacturer = value;
                            break;
                        case nameof(AndroidDevice.AndroidVersion):
                            device.AndroidVersion = value;
                            break;
                        case "SdkVersion":
                            if (int.TryParse(value, out int sdk))
                            {
                                device.SdkVersion = sdk;
                            }
                            break;
                    }
                }
            }

            // 设置设备名称
            if (string.IsNullOrEmpty(device.DeviceName))
            {
                device.DeviceName = $"{device.Manufacturer} {device.Model}".Trim();
            }

            return device;
        }

        /// <summary>
        /// 执行端口转发
        /// </summary>
        public async Task<bool> ForwardPortAsync(
            string deviceId,
            int localPort,
            int remotePort,
            CancellationToken cancellationToken = default)
        {
            var (success, _, error) = await ExecuteCommandAsync(
                $"-s {deviceId} forward tcp:{localPort} tcp:{remotePort}",
                cancellationToken: cancellationToken);

            if (success)
            {
                LogHelper.Info($"Port forward established: localhost:{localPort} -> {deviceId}:{remotePort}");
            }
            else
            {
                LogHelper.Error($"Failed to forward port: {error}");
            }

            return success;
        }

        /// <summary>
        /// 移除端口转发
        /// </summary>
        public async Task<bool> RemoveForwardAsync(
            string deviceId,
            int localPort,
            CancellationToken cancellationToken = default)
        {
            var (success, _, _) = await ExecuteCommandAsync(
                $"-s {deviceId} forward --remove tcp:{localPort}",
                cancellationToken: cancellationToken);

            if (success)
            {
                LogHelper.Info($"Port forward removed: localhost:{localPort}");
            }

            return success;
        }

        /// <summary>
        /// 移除所有端口转发
        /// </summary>
        public async Task<bool> RemoveAllForwardsAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            var (success, _, _) = await ExecuteCommandAsync(
                $"-s {deviceId} forward --remove-all",
                cancellationToken: cancellationToken);

            return success;
        }

        /// <summary>
        /// 检查设备是否已连接
        /// </summary>
        public async Task<bool> IsDeviceConnectedAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var devices = await GetDevicesAsync(cancellationToken);
            return devices.Any(d => d.DeviceId == deviceId && d.IsAuthorized);
        }
    }
}
