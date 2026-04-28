using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Server.Services;

namespace Server.Hubs;

[Authorize]
public class VotingHub : Hub
{
    private readonly ConnectionRegistry _registry;
    private readonly IHubContext<VotingHub> _hubContext;

    public VotingHub(ConnectionRegistry registry, IHubContext<VotingHub> hubContext)
    {
        _registry = registry;
        _hubContext = hubContext;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user == null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Connection aborted: no user principal");
            Context.Abort();
            return;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        var stationId = user.FindFirst("station")?.Value;
        var county = user.FindFirst("county")?.Value;
        var constituency = user.FindFirst("constituency")?.Value;

        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(county) || string.IsNullOrWhiteSpace(constituency))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Connection aborted: missing claims (role='{role}', county='{county}', constituency='{constituency}')");
            Context.Abort();
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Incoming connection {Context.ConnectionId} role={role} county={county} constituency={constituency}");

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.County(county));
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.CountyConstituency(county, constituency));

        var deviceId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();

        if (role.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            var officialId = user.FindFirst("officialId")?.Value;
            if (string.IsNullOrWhiteSpace(officialId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Official connection aborted: missing officialId claim");
                Context.Abort();
                return;
            }

            // If the official is reconnecting, cancel any pending voter-disconnect timer.
            _registry.CancelOfficialDisconnectTimer(officialId);

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Official(officialId));
            _registry.Add(new Server.Services.ConnectionInfo(Context.ConnectionId, "official", officialId, officialId, stationId, county, constituency, null));
        }
        else if (role.Equals("voter", StringComparison.OrdinalIgnoreCase))
        {
            var voterId = user.FindFirst("voterId")?.Value;
            var officialId = user.FindFirst("officialId")?.Value;
            if (string.IsNullOrWhiteSpace(voterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Voter connection aborted: missing voterId claim");
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Voter(voterId));

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.VoterDevice(voterId, deviceId));

                if (!string.IsNullOrWhiteSpace(officialId) && int.TryParse(voterId, out var parsedVoterId))
                {
                    await Clients.Group(RealtimeGroups.Official(officialId)).SendAsync("official.v1.devicePresenceChanged", new
                    {
                        voterId = parsedVoterId,
                        deviceId,
                        state = "online",
                        status = "Connected",
                        timestamp = DateTime.UtcNow,
                        county,
                        constituency
                    });
                }
            }

            _registry.Add(new Server.Services.ConnectionInfo(Context.ConnectionId, "voter", voterId, officialId, stationId, county, constituency, deviceId));
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Connection aborted: unsupported role '{role}'");
            Context.Abort();
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Connection established: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Connection disconnected: {Context.ConnectionId}. Reason: {exception?.Message ?? "normal close"}");

        if (_registry.Remove(Context.ConnectionId, out var disconnectedInfo) && disconnectedInfo != null)
        {
            if (disconnectedInfo.Role.Equals("official", StringComparison.OrdinalIgnoreCase))
            {
                var officialId = disconnectedInfo.OfficialId;
                if (!string.IsNullOrWhiteSpace(officialId))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Official {officialId} disconnected. Starting 45s grace period before disconnecting voter devices.");

                    var hubContext = _hubContext;
                    var registry = _registry;

                    registry.StartOfficialDisconnectTimer(officialId, TimeSpan.FromSeconds(10), async () =>
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Grace period expired for official {officialId}. Disconnecting voter devices.");
                        await SendOfficialDisconnectedToVotersAsync(hubContext.Clients, registry, officialId);
                    });
                }
            }
            else if (disconnectedInfo.Role.Equals("voter", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(disconnectedInfo.DeviceId) &&
                     !string.IsNullOrWhiteSpace(disconnectedInfo.OfficialId) &&
                     int.TryParse(disconnectedInfo.UserId, out var parsedVoterId))
            {
                await Clients.Group(RealtimeGroups.Official(disconnectedInfo.OfficialId)).SendAsync("official.v1.devicePresenceChanged", new
                {
                    voterId = parsedVoterId,
                    deviceId = disconnectedInfo.DeviceId,
                    state = "offline",
                    status = "Disconnected",
                    timestamp = DateTime.UtcNow,
                    county = disconnectedInfo.County,
                    constituency = disconnectedInfo.Constituency
                });
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called by the official app before intentionally closing (logout / app shutdown).
    /// Immediately disconnects all voter devices linked to this official without waiting
    /// for the grace-period timer, and cancels any pending timer.
    /// </summary>
    public async Task OfficialLogout()
    {
        var user = Context.User;
        if (user == null) return;

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        if (!string.Equals(role, "official", StringComparison.OrdinalIgnoreCase)) return;

        var officialId = user.FindFirst("officialId")?.Value;
        if (string.IsNullOrWhiteSpace(officialId)) return;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] OfficialLogout called for {officialId}. Immediately disconnecting voter devices.");

        _registry.CancelOfficialDisconnectTimer(officialId);
        await SendOfficialDisconnectedToVotersAsync(_hubContext.Clients, _registry, officialId);
    }

    private static async Task SendOfficialDisconnectedToVotersAsync(
        IHubClients clients,
        ConnectionRegistry registry,
        string officialId)
    {
        var voterConnections = registry.GetVoterConnectionsForOfficial(officialId).ToList();
        if (voterConnections.Count == 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] No voter devices to disconnect for official {officialId}.");
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [VotingHub] Sending officialDisconnected to {voterConnections.Count} voter device(s) for official {officialId}.");

        var connectionIds = voterConnections.Select(v => v.ConnectionId).ToList();
        await clients.Clients(connectionIds).SendAsync("voter.v1.officialDisconnected", new
        {
            officialId,
            reason = "official_session_ended",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Called by the official app after connecting to retrieve all voter devices that are
    /// already connected to the hub (e.g. devices that reconnected before the official re-logged in).
    /// </summary>
    public Task<IEnumerable<object>> GetConnectedVoterDevices()
    {
        var user = Context.User;
        if (user == null)
            return Task.FromResult(Enumerable.Empty<object>());

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        if (!string.Equals(role, "official", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Enumerable.Empty<object>());

        var officialId = user.FindFirst("officialId")?.Value;
        if (string.IsNullOrWhiteSpace(officialId))
            return Task.FromResult(Enumerable.Empty<object>());

        var devices = _registry.GetVoterConnectionsForOfficial(officialId)
            .Where(c => !string.IsNullOrWhiteSpace(c.DeviceId) && int.TryParse(c.UserId, out _))
            .Select(c => (object)new
            {
                voterId = int.Parse(c.UserId),
                deviceId = c.DeviceId!,
                state = "online",
                status = "Connected",
                timestamp = DateTime.UtcNow,
                county = c.County,
                constituency = c.Constituency
            });

        return Task.FromResult(devices);
    }

    public async Task UpdateDeviceStatus(string deviceId, string status)
    {
        var user = Context.User;
        if (user == null)
        {
            return;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        if (!string.Equals(role, "voter", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var voterId = user.FindFirst("voterId")?.Value;
        var officialId = user.FindFirst("officialId")?.Value;
        var county = user.FindFirst("county")?.Value;
        var constituency = user.FindFirst("constituency")?.Value;

        if (!int.TryParse(voterId, out var parsedVoterId) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(status) ||
            string.IsNullOrWhiteSpace(officialId) ||
            string.IsNullOrWhiteSpace(county) ||
            string.IsNullOrWhiteSpace(constituency))
        {
            return;
        }

        await Clients.Group(RealtimeGroups.Official(officialId)).SendAsync("official.v1.deviceStatusReceived", new
        {
            voterId = parsedVoterId,
            deviceId,
            status,
            timestamp = DateTime.UtcNow,
            county,
            constituency
        });
    }
}
