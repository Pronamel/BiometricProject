using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Services;

public class OfficialService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> _countyVoterCodes;
    private readonly ConcurrentDictionary<string, DateTime> _activeVotingSessions;
    private readonly ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> _activeOfficials;
    private readonly ApplicationDbContext _dbContext;

    public OfficialService(
        ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> countyVoterCodes,
        ConcurrentDictionary<string, DateTime> activeVotingSessions,
        ConcurrentDictionary<string, (string OfficialId, string StationId, string Constituency, DateTime LoginTime, List<int> ConnectedVoters)> activeOfficials,
        ApplicationDbContext dbContext)
    {
        _countyVoterCodes = countyVoterCodes;
        _activeVotingSessions = activeVotingSessions;
        _activeOfficials = activeOfficials;
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

    public async Task<bool> ValidateOfficialCredentialsAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Validating official credentials:");
            Console.WriteLine($"  Username: '{username}'");

            // Query the database for the official
            var official = await _dbContext.Officials
                .FirstOrDefaultAsync(o => o.Username == username);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Official '{username}' not found in database");
                return false;
            }

            bool passwordMatch = PasswordHasher.VerifyPassword(official.PasswordHash, password);

            if (passwordMatch)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official '{username}' credentials verified");
                return true;
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Incorrect password for official '{username}'");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error validating credentials: {ex.Message}");
            return false;
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

    public bool IsOfficialAlreadyLoggedIn(string officialId)
    {
        var isLoggedIn = _activeOfficials.Values.Any(v => v.OfficialId == officialId);
        
        if (isLoggedIn)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Official {officialId} is already logged in elsewhere");
        }
        
        return isLoggedIn;
    }
}