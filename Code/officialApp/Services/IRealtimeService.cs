using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IRealtimeService
{
    bool IsConnected { get; }

    event Action<List<string>>? VoterRequestsReceived;
    event Action<VoteInfo>? VoteReceived;
    event Action<DeviceStatus>? DeviceStatusReceived;
    event Action<DevicePresenceUpdate>? DevicePresenceChanged;
    event Action<string>? ConnectionStateChanged;
    event Action? ServerShutdown;

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task<List<DevicePresenceUpdate>> GetConnectedVoterDevicesAsync();
    Task SendLogoutNotificationAsync();
    Task DisconnectAsync();
}
