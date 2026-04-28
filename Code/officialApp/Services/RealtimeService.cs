using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using officialApp.Models;

namespace officialApp.Services;

public class RealtimeService : IRealtimeService
{
    private readonly IApiService _apiService;
    private HubConnection? _hubConnection;

    public event Action<List<string>>? VoterRequestsReceived;
    public event Action<VoteInfo>? VoteReceived;
    public event Action<DeviceStatus>? DeviceStatusReceived;
    public event Action<DevicePresenceUpdate>? DevicePresenceChanged;
    public event Action<string>? ConnectionStateChanged;
    public event Action? ServerShutdown;

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
                    // Prefer websocket but allow long-polling fallback for restrictive proxies.
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.HandshakeTimeout = TimeSpan.FromSeconds(15);
            _hubConnection.ServerTimeout = TimeSpan.FromSeconds(16);
            _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(4);

            RegisterHandlers(_hubConnection);
        }

        // Avoid canceling the initial handshake from short-lived UI tokens.
        await _hubConnection.StartAsync();
        ConnectionStateChanged?.Invoke("Connected");
        return true;
    }

    public async Task<List<DevicePresenceUpdate>> GetConnectedVoterDevicesAsync()
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            return new List<DevicePresenceUpdate>();
        }

        try
        {
            return await _hubConnection.InvokeAsync<List<DevicePresenceUpdate>>("GetConnectedVoterDevices");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RealtimeService] GetConnectedVoterDevices failed: {ex.Message}");
            return new List<DevicePresenceUpdate>();
        }
    }

    public async Task SendLogoutNotificationAsync()
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("OfficialLogout");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RealtimeService] OfficialLogout notification failed: {ex.Message}");
        }
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

        // Null the connection so the next ConnectAsync builds a fresh one with a fresh
        // token and clean handler state — avoids reusing a stale object from a previous session.
        _hubConnection = null;

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

        connection.On<DevicePresenceUpdate>("official.v1.devicePresenceChanged", payload =>
        {
            DevicePresenceChanged?.Invoke(payload);
        });

        connection.On<object>("server.v1.shutdown", _ =>
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RealtimeService] Received server.v1.shutdown.");
            ServerShutdown?.Invoke();
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
