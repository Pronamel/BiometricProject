using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
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
