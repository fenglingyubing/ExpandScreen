using System.Diagnostics;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    /// <summary>
    /// Windows 防火墙规则管理（best-effort）。
    /// 注意：添加规则通常需要管理员权限；失败不会阻塞功能，只会记录日志。
    /// </summary>
    public static class WindowsFirewallRuleManager
    {
        public static bool IsSupported => OperatingSystem.IsWindows();

        public static async Task<bool> TryEnsureInboundPortRuleAsync(string ruleName, int port, string protocol)
        {
            if (!IsSupported)
            {
                return false;
            }

            if (port <= 0)
            {
                return false;
            }

            // netsh 的 protocol 参数为 TCP/UDP
            string normalizedProtocol = protocol.ToUpperInvariant() switch
            {
                "TCP" => "TCP",
                "UDP" => "UDP",
                _ => throw new ArgumentException($"Unsupported protocol: {protocol}", nameof(protocol))
            };

            // 先尝试删除同名规则，避免重复/冲突
            await TryDeleteRuleAsync(ruleName);

            // 添加入站规则
            string args =
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol={normalizedProtocol} localport={port}";

            return await TryRunNetshAsync(args);
        }

        public static async Task<bool> TryDeleteRuleAsync(string ruleName)
        {
            if (!IsSupported)
            {
                return false;
            }

            string args = $"advfirewall firewall delete rule name=\"{ruleName}\"";
            return await TryRunNetshAsync(args);
        }

        private static async Task<bool> TryRunNetshAsync(string arguments)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    LogHelper.Warning($"netsh failed (exit={process.ExitCode}): netsh {arguments}\n{stderr}".Trim());
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    LogHelper.Debug(stdout.Trim());
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"netsh execution failed: {ex.Message}");
                return false;
            }
        }
    }
}

