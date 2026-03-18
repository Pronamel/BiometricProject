using System.Collections.Concurrent;
using Server.Data;

namespace Server.Services;

public class OfficialService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>> _countyChannels;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> _countyVoterCodes;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>> _countyActiveConnections;
    private readonly ConcurrentDictionary<string, DateTime> _activeVotingSessions;
    private readonly ApplicationDbContext _dbContext;

    public OfficialService(
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>> countyChannels,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> countyVoterCodes,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>> countyActiveConnections,
        ConcurrentDictionary<string, DateTime> activeVotingSessions,
        ApplicationDbContext dbContext)
    {
        _countyChannels = countyChannels;
        _countyVoterCodes = countyVoterCodes;
        _countyActiveConnections = countyActiveConnections;
        _activeVotingSessions = activeVotingSessions;
        _dbContext = dbContext;
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

    public async Task<(bool Success, List<string> Requests)> WaitForVoterRequests(string county, string constituency, string officialId, TimeSpan timeout)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official {officialId} waiting for voter requests in county: {county}, constituency: {constituency}");

        var countyDict = _countyChannels.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentBag<string>>());
        var countyRequests = countyDict.GetOrAdd(constituency, _ => new ConcurrentBag<string>());

        var countyConnDict = _countyActiveConnections.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>?>());
        var constituencyConnDict = countyConnDict.GetOrAdd(constituency, _ => new ConcurrentDictionary<string, TaskCompletionSource<List<string>>>());

        // Create task completion source for this specific official
        var tcs = new TaskCompletionSource<List<string>>();
        constituencyConnDict[officialId] = tcs;

        var startTime = DateTime.Now;

        // First check if there are already pending requests for this county+constituency
        if (!countyRequests.IsEmpty)
        {
            var requests = new List<string>();
            while (countyRequests.TryTake(out string? request))
            {
                if (request != null) requests.Add(request);
            }

            if (requests.Count > 0)
            {
                // ❌ DON'T REMOVE - Let them stay registered
                // constituencyConnDict.TryRemove(officialId, out _);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {requests.Count} pending voter requests to official {officialId} in {county}/{constituency}");
                return (true, requests);
            }
        }

        // Wait for new requests or timeout
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await tcs.Task.WaitAsync(cts.Token);
            
            // ❌ DON'T REMOVE - Let them stay registered
            // constituencyConnDict.TryRemove(officialId, out _);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending {result.Count} new voter requests to official {officialId} in {county}/{constituency}");
            return (true, result);
        }
        catch (OperationCanceledException)
        {
            // ✅ ONLY remove on timeout (they stopped listening)
            constituencyConnDict.TryRemove(officialId, out _);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timeout waiting for voter requests in {county}/{constituency} for official {officialId}");
            return (false, new List<string>());
        }
    }

    public (bool Success, string Code) GenerateAccessCode(string voterId, string county, string constituency)
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

            // Ensure county+constituency codes dictionary exists
            var countyDict = _countyVoterCodes.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>());
            var constituencyCodesDict = countyDict.GetOrAdd(constituency, _ => new ConcurrentDictionary<string, string>());

            // Generate secure 6-digit code
            var code = Random.Shared.Next(100000, 999999).ToString();
            constituencyCodesDict[voterId] = code;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official generated code {code} for voter {voterId} in {county}/{constituency}");
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