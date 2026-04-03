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
    event Action<string>? ConnectionStateChanged;

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
