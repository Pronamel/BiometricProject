using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Server.Services;

namespace Server.Hubs;

[Authorize]
public class VotingHub : Hub
{
    private readonly ConnectionRegistry _registry;

    public VotingHub(ConnectionRegistry registry)
    {
        _registry = registry;
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

        if (_registry.Remove(Context.ConnectionId, out var disconnectedInfo) &&
            disconnectedInfo != null &&
            disconnectedInfo.Role.Equals("voter", StringComparison.OrdinalIgnoreCase) &&
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

        await base.OnDisconnectedAsync(exception);
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
