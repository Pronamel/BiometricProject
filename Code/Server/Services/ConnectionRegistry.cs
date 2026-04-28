using System.Collections.Concurrent;
using System.Linq;

namespace Server.Services;

public class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _officialDisconnectTimers = new();

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

    public IEnumerable<ConnectionInfo> GetVoterConnectionsForOfficial(string officialId)
    {
        return _connections.Values
            .Where(c => c.Role.Equals("voter", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.OfficialId, officialId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void StartOfficialDisconnectTimer(string officialId, TimeSpan delay, Func<Task> onExpired)
    {
        // Cancel any existing pending timer for this official before starting a new one.
        if (_officialDisconnectTimers.TryRemove(officialId, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _officialDisconnectTimers[officialId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                _officialDisconnectTimers.TryRemove(officialId, out _);
                await onExpired();
            }
            catch (OperationCanceledException)
            {
                // Timer was cancelled – official reconnected or called logout explicitly.
            }
        });
    }

    public void CancelOfficialDisconnectTimer(string officialId)
    {
        if (_officialDisconnectTimers.TryRemove(officialId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

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
