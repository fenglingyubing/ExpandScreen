using System.Threading;
using System.Threading.Tasks;

namespace ExpandScreen.Services.Connection
{
    public interface IAdbCommandRunner
    {
        Task<(bool success, string output, string error)> RunAsync(
            string adbPath,
            string arguments,
            int timeoutMs,
            CancellationToken cancellationToken = default);
    }
}

