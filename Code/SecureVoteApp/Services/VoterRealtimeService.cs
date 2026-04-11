using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class VoterRealtimeService : IVoterRealtimeService
{
    private readonly IApiService _apiService;
    private HubConnection? _hubConnection;

    public event Action<VoterCommandResponse>? CommandReceived;
    public event Action<CodeWaitResponse>? AccessCodeReceived;
    public event Action<string>? ConnectionStateChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public VoterRealtimeService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<bool> ConnectAsync(string? deviceId, CancellationToken cancellationToken = default)
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

        var hubUrl = _apiService.GetRealtimeHubUrl();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var separator = hubUrl.Contains('?') ? "&" : "?";
            hubUrl = $"{hubUrl}{separator}deviceId={Uri.EscapeDataString(deviceId)}";
        }

        if (_hubConnection == null)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
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

    public async Task<bool> SendDeviceStatusAsync(string deviceId, string status, CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        try
        {
            await _hubConnection.InvokeAsync("UpdateDeviceStatus", deviceId, status, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            ConnectionStateChanged?.Invoke($"Status send failed: {ex.Message}");
            return false;
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

        ConnectionStateChanged?.Invoke("Disconnected");
    }

    private void RegisterHandlers(HubConnection connection)
    {
        connection.On<VoterCommandResponse>("voter.v1.deviceCommandReceived", payload =>
        {
            CommandReceived?.Invoke(payload);
        });

        connection.On<CodeWaitResponse>("voter.v1.accessCodeGenerated", payload =>
        {
            AccessCodeReceived?.Invoke(payload);
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
