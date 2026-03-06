using System.Collections.Concurrent;

namespace Server.Services;

public class VoterService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _countyChannels;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _countyVoterCodes;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<List<string>>>> _countyActiveConnections;
    private readonly ConcurrentDictionary<string, DateTime> _activeVotingSessions;

    public VoterService(
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

    public async Task<bool> RequestAccess(string voterId, string county, string deviceName = "Unknown")
    {
        try
        {
            // Add validation logic here
            if (string.IsNullOrEmpty(voterId))
                return false;

            if (string.IsNullOrEmpty(county))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot process request: Invalid county for voter {voterId}");
                return false;
            }

            // Check if voter already has an active session
            if (_activeVotingSessions.ContainsKey(voterId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} already has active session");
                return false;
            }

            // Add request to county-specific channel
            var requestMessage = $"Voter {voterId} requesting access from {deviceName}";
            
            // Get or create county channel
            var countyRequests = _countyChannels.GetOrAdd(county, _ => new ConcurrentBag<string>());
            countyRequests.Add(requestMessage);

            // Notify any waiting officials in this county
            var countyConnections = _countyActiveConnections.GetOrAdd(county, _ => new ConcurrentDictionary<string, TaskCompletionSource<List<string>>>());
            
            if (countyConnections.Any())
            {
                // Get first available official connection
                var firstConnection = countyConnections.First();
                if (countyConnections.TryRemove(firstConnection.Key, out var tcs))
                {
                    // Send this request to waiting official
                    tcs.SetResult(new List<string> { requestMessage });
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Routed voter request to waiting official {firstConnection.Key} in {county}");
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {requestMessage} in {county}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error processing voter request: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string? Code)> WaitForAccessCode(string voterId, string county, TimeSpan timeout)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter {voterId} waiting for access code in {county}");

        if (string.IsNullOrEmpty(county))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cannot wait for code: Invalid county for voter {voterId}");
            return (false, null);
        }

        var startTime = DateTime.Now;

        while (DateTime.Now - startTime < timeout)
        {
            // Get county-specific voter codes
            var countyVoterCodes = _countyVoterCodes.GetOrAdd(county, _ => new ConcurrentDictionary<string, string>());
            
            // Check if voter has a pending code
            if (countyVoterCodes.TryGetValue(voterId, out string? code))
            {
                countyVoterCodes.TryRemove(voterId, out _); // One-time use
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending code {code} to voter {voterId} from {county}");
                return (true, code);
            }

            await Task.Delay(500); // Check every 0.5 seconds
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timeout waiting for code for voter {voterId} in {county}");
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