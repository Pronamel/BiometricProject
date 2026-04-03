using System;
using System.Threading;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IVoterRealtimeService
{
    bool IsConnected { get; }

    event Action<VoterCommandResponse>? CommandReceived;
    event Action<CodeWaitResponse>? AccessCodeReceived;
    event Action<string>? ConnectionStateChanged;

    Task<bool> ConnectAsync(string? deviceId, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
