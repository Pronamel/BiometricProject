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
            Context.Abort();
            return;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        var county = user.FindFirst("county")?.Value;
        var constituency = user.FindFirst("constituency")?.Value;

        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(county) || string.IsNullOrWhiteSpace(constituency))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.County(county));
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.CountyConstituency(county, constituency));

        var deviceId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();

        if (role.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            var officialId = user.FindFirst("officialId")?.Value;
            if (string.IsNullOrWhiteSpace(officialId))
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Official(officialId));
            _registry.Add(new Server.Services.ConnectionInfo(Context.ConnectionId, "official", officialId, county, constituency, null));
        }
        else if (role.Equals("voter", StringComparison.OrdinalIgnoreCase))
        {
            var voterId = user.FindFirst("voterId")?.Value;
            if (string.IsNullOrWhiteSpace(voterId))
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Voter(voterId));

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.VoterDevice(voterId, deviceId));
            }

            _registry.Add(new Server.Services.ConnectionInfo(Context.ConnectionId, "voter", voterId, county, constituency, deviceId));
        }
        else
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }
}
