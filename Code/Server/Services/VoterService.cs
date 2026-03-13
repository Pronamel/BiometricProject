using System.Collections.Concurrent;

namespace Server.Services;

public class VoterService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>> _countyChannels;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> _countyVoterCodes;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>> _countyActiveConnections;
    private readonly ConcurrentDictionary<string, DateTime> _activeVotingSessions;

    public VoterService(
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentBag<string>>> countyChannels,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> countyVoterCodes,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>>> countyActiveConnections,
        ConcurrentDictionary<string, DateTime> activeVotingSessions)
    {
        _countyChannels = countyChannels;
        _countyVoterCodes = countyVoterCodes;
        _countyActiveConnections = countyActiveConnections;
        _activeVotingSessions = activeVotingSessions;
    }

    public async Task<bool> RequestAccess(string voterId, string county, string constituency, string deviceName = "Unknown")
    {
        try
        {
            if (string.IsNullOrEmpty(voterId))
                return false;

            if (string.IsNullOrEmpty(county))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot process request: Invalid county for voter {voterId}");
                return false;
            }

            if (_activeVotingSessions.ContainsKey(voterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} already has active session");
                return false;
            }

            var requestMessage = $"Voter {voterId} requesting access from {deviceName}";

            var countyDict = _countyChannels.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentBag<string>>());
            var constituencyRequests = countyDict.GetOrAdd(constituency, _ => new ConcurrentBag<string>());
            constituencyRequests.Add(requestMessage);

            var countyConnDict = _countyActiveConnections.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>?>());
            var constituencyConnDict = countyConnDict.GetOrAdd(constituency, _ => new ConcurrentDictionary<string, TaskCompletionSource<List<string>>>());

            // Send to all officials currently registered in the channel
            foreach (var kvp in constituencyConnDict.ToList())
            {
                if (kvp.Value != null && !kvp.Value.Task.IsCompleted)
                {
                    kvp.Value.SetResult(new List<string> { requestMessage });
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Routed voter request to connected official {kvp.Key} in {county}/{constituency}");
                    
                    // ❌ REMOVE THIS LINE - Don't create a new TCS here!
                    // constituencyConnDict[kvp.Key] = new TaskCompletionSource<List<string>>();
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {requestMessage} in {county}/{constituency}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error processing voter request: {ex.Message}");
            return false;
        }
    }
    public async Task<(bool Success, string? Code)> WaitForAccessCode(string voterId, string county, string constituency, TimeSpan timeout)
    {
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} waiting for access code in {county}/{constituency}");

        if (string.IsNullOrEmpty(county) || string.IsNullOrEmpty(constituency))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot wait for code: Invalid county or constituency for voter {voterId}");
            return (false, null);
        }

        var startTime = DateTime.Now;

        while (DateTime.Now - startTime < timeout)
        {
            // Get or create county+constituency voter codes
            var countyVoterCodes = _countyVoterCodes.GetOrAdd(county, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>());
            var constituencyVoterCodes = countyVoterCodes.GetOrAdd(constituency, _ => new ConcurrentDictionary<string, string>());

            // Check if voter has a pending code
            if (constituencyVoterCodes.TryGetValue(voterId, out string? code))
            {
                constituencyVoterCodes.TryRemove(voterId, out _); // One-time use
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending code {code} to voter {voterId} from {county}/{constituency}");
                return (true, code);
            }

            await Task.Delay(500); // Check every 0.5 seconds
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timeout waiting for code for voter {voterId} in {county}/{constituency}");
        return (false, null);
    }

    public bool ValidateVoterId(string voterId)
    {
        // Add more sophisticated validation here
        return !string.IsNullOrEmpty(voterId) && voterId.Length >= 5;
    }

    public string CreateVotingSession(string voterId)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        _activeVotingSessions[voterId] = DateTime.UtcNow.AddHours(4);
        
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Created voting session {sessionId} for voter {voterId}");
        return sessionId;
    }
}