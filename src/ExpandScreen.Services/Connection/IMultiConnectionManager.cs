namespace ExpandScreen.Services.Connection
{
    public interface IMultiConnectionManager : IDisposable
    {
        IReadOnlyCollection<DeviceSessionSnapshot> Sessions { get; }

        event EventHandler<DeviceSessionSnapshot>? SessionUpdated;

        Task<ConnectDeviceResult> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

        Task<bool> DisconnectAsync(string deviceId);

        Task DisconnectAllAsync();
    }
}

