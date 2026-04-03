using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using officialApp.Models;

namespace officialApp.Services;

public class RealtimeService : IRealtimeService
{
    private readonly IApiService _apiService;
    private HubConnection? _hubConnection;

    public event Action<List<string>>? VoterRequestsReceived;
    public event Action<VoteInfo>? VoteReceived;
    public event Action<DeviceStatus>? DeviceStatusReceived;
    public event Action<string>? ConnectionStateChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public RealtimeService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return true;
        }

        var token = _apiService.GetAuthToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            ConnectionStateChanged?.Invoke("Not authenticated");
            return false;
        }

        if (_hubConnection == null)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_apiService.GetRealtimeHubUrl(), options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(_apiService.GetAuthToken());
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers(_hubConnection);
        }

        await _hubConnection.StartAsync(cancellationToken);
        ConnectionStateChanged?.Invoke("Connected");
        return true;
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection == null)
        {
            return;
        }

        if (_hubConnection.State != HubConnectionState.Disconnected)
        {
            await _hubConnection.StopAsync();
        }

        ConnectionStateChanged?.Invoke("Disconnected");
    }

    private void RegisterHandlers(HubConnection connection)
    {
        connection.On<OfficialVoterRequestEvent>("official.v1.voterRequestReceived", payload =>
        {
            var requests = new List<string>();
            if (!string.IsNullOrWhiteSpace(payload.Request))
            {
                requests.Add(payload.Request);
            }

            if (requests.Count > 0)
            {
                VoterRequestsReceived?.Invoke(requests);
            }
        });

        connection.On<VoteInfo>("official.v1.voteReceived", payload =>
        {
            VoteReceived?.Invoke(payload);
        });

        connection.On<DeviceStatus>("official.v1.deviceStatusReceived", payload =>
        {
            DeviceStatusReceived?.Invoke(payload);
        });

        connection.Reconnecting += error =>
        {
            ConnectionStateChanged?.Invoke($"Reconnecting: {error?.Message ?? "connection interrupted"}");
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            ConnectionStateChanged?.Invoke("Connected");
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            ConnectionStateChanged?.Invoke($"Disconnected: {error?.Message ?? "connection closed"}");
            return Task.CompletedTask;
        };
    }
}

public class OfficialVoterRequestEvent
{
    public string Request { get; set; } = string.Empty;
    public string VoterId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string Constituency { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
