using System.Collections.Concurrent;

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
}

public record ConnectionInfo(
    string ConnectionId,
    string Role,
    string UserId,
    string County,
    string Constituency,
    string? DeviceId
);
