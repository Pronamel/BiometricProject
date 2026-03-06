using System.Collections.Concurrent;

namespace Server.Services;

public class OfficialService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _countyChannels;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _countyVoterCodes;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>> _countyActiveConnections;
    private readonly ConcurrentDictionary<string, DateTime> _activeVotingSessions;

    public OfficialService(
        ConcurrentDictionary<string, ConcurrentBag<string>> countyChannels,
        ConcurrentDictionary<string, ConcurrentDictionary<string, string>> countyVoterCodes,
        ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>> countyActiveConnections,
        ConcurrentDictionary<string, DateTime> activeVotingSessions)
    {
        _countyChannels = countyChannels;
        _countyVoterCodes = countyVoterCodes;
        _countyActiveConnections = countyActiveConnections;
        _activeVotingSessions = activeVotingSessions;
    }

    public bool ValidateOfficialLogin(string officialId, string stationId, string? password = null)
    {
        // Simple validation - in production, check against database/directory
        var stationValid = stationId?.StartsWith("PollingStation") == true;
        var officialValid = !string.IsNullOrEmpty(officialId);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Validating official login:");
        Console.WriteLine($"  Official ID: '{officialId ?? "NULL"}'");
        Console.WriteLine($"  Station ID: '{stationId ?? "NULL"}'");
        Console.WriteLine($"  Station valid: {stationValid}");
        Console.WriteLine($"  Official valid: {officialValid}");

        return stationValid && officialValid;
    }

    public async Task<(bool Success, List<string> Requests)> WaitForVoterRequests(string county, string officialId, TimeSpan timeout)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} waiting for voter requests in county: {county}");

        // Ensure county channel exists
        var countyRequests = _countyChannels.GetOrAdd(county, _ => new ConcurrentBag<string>());
        
        // Ensure county connections dictionary exists
        var countyConnections = _countyActiveConnections.GetOrAdd(county, _ => new ConcurrentDictionary<string, TaskCompletionSource<List<string>>>());
        
        // Create task completion source for this specific official
        var tcs = new TaskCompletionSource<List<string>>();
        countyConnections[officialId] = tcs;
        
        var startTime = DateTime.Now;

        // First check if there are already pending requests for this county
        if (!countyRequests.IsEmpty)
        {
            var requests = new List<string>();
            while (countyRequests.TryTake(out string? request))
            {
                if (request != null) requests.Add(request);
            }

            if (requests.Count > 0)
            {
                countyConnections.TryRemove(officialId, out _); // Cleanup
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {requests.Count} pending voter requests to official {officialId} in {county}");
                return (true, requests);
            }
        }

        // Wait for new requests or timeout
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await tcs.Task.WaitAsync(cts.Token);
            countyConnections.TryRemove(officialId, out _); // Cleanup
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {result.Count} new voter requests to official {officialId} in {county}");
            return (true, result);
        }
        catch (OperationCanceledException)
        {
            countyConnections.TryRemove(officialId, out _); // Cleanup
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timeout waiting for voter requests in {county} for official {officialId}");
            return (false, new List<string>());
        }
    }

    public (bool Success, string Code) GenerateAccessCode(string voterId, string county)
    {
        try
        {
            // Validate voter ID
            if (string.IsNullOrEmpty(voterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot generate code: Invalid voter ID");
                return (false, string.Empty);
            }

            // Validate county
            if (string.IsNullOrEmpty(county))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot generate code: Invalid county");
                return (false, string.Empty);
            }

            // Ensure county codes dictionary exists
            var countyCodesDict = _countyVoterCodes.GetOrAdd(county, _ => new ConcurrentDictionary<string, string>());

            // Generate secure 6-digit code
            var code = Random.Shared.Next(100000, 999999).ToString();
            countyCodesDict[voterId] = code;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official generated code {code} for voter {voterId} in {county}");
            return (true, code);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error generating code: {ex.Message}");
            return (false, string.Empty);
        }
    }

    public List<string> GetActiveVotingSessions()
    {
        var now = DateTime.UtcNow;
        var activeSessions = new List<string>();

        foreach (var session in _activeVotingSessions)
        {
            if (session.Value > now)
            {
                activeSessions.Add($"Voter {session.Key} - Expires: {session.Value:HH:mm:ss}");
            }
        }

        return activeSessions;
    }

    public bool RevokeVoterSession(string voterId)
    {
        var removed = _activeVotingSessions.TryRemove(voterId, out _);
        if (removed)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Revoked session for voter {voterId}");
        }
        return removed;
    }
}