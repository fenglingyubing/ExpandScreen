using System.Diagnostics;
using System.Text;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Connection
{
    public class ProcessAdbCommandRunner : IAdbCommandRunner
    {
        public async Task<(bool success, string output, string error)> RunAsync(
            string adbPath,
            string arguments,
            int timeoutMs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
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

                return (success, output, error);
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error executing ADB command: adb {arguments}", ex);
                return (false, "", ex.Message);
            }
        }
    }
}

