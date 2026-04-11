using System.Collections.Concurrent;
using System.Linq;

namespace Server.Services;

public class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();

    public void Add(ConnectionInfo info)
    {
        _connections[info.ConnectionId] = info;
    }

    public bool Remove(string connectionId, out ConnectionInfo? info)
    {
        if (_connections.TryRemove(connectionId, out var removed))
        {
            info = removed;
            return true;
        }

        info = null;
        return false;
    }

    public int Count => _connections.Count;

    public int CountConnectedPollingStations()
    {
        return _connections.Values
            .Where(c => c.Role.Equals("official", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.StationId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public IEnumerable<string> GetConnectedStationIds()
    {
        return _connections.Values
            .Where(c => c.Role.Equals("official", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.StationId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!) // null-forgiving operator since we filtered nulls
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

public record ConnectionInfo(
    string ConnectionId,
    string Role,
    string UserId,
    string? OfficialId,
    string? StationId,
    string County,
    string Constituency,
    string? DeviceId
);
