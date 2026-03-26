using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models.Entities;

namespace Server.Services;

public class DatabaseService
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Voter>> GetAllVotersAsync()
    {
        return await _dbContext.Voters.ToListAsync();
    }

    public async Task<Official?> GetOfficialByCredentialsAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Querying Officials for username: {username}");
            
            var official = await _dbContext.Officials
                .Include(o => o.AssignedPollingStation)
                .ThenInclude(ps => ps!.Constituency)
                .FirstOrDefaultAsync(o => o.Username == username && o.PasswordHash == password);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No official found with username '{username}' and password '{password}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official found: {official.OfficialId}");
            Console.WriteLine($"    AssignedPollingStationId: {official.AssignedPollingStationId}");
            Console.WriteLine($"    AssignedPollingStation is null: {official.AssignedPollingStation == null}");
            if (official.AssignedPollingStation != null)
            {
                Console.WriteLine($"    PollingStation County: {official.AssignedPollingStation.County}");
                Console.WriteLine($"    Constituency is null: {official.AssignedPollingStation.Constituency == null}");
                if (official.AssignedPollingStation.Constituency != null)
                {
                    Console.WriteLine($"    Constituency Name: {official.AssignedPollingStation.Constituency.Name}");
                }
            }

            return official;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error retrieving official by credentials: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<bool> UpdateOfficialFingerprintAsync(string username, string password, byte[] fingerprintData)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 [DatabaseService] UpdateOfficialFingerprintAsync called");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Username: '{username}'");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Password: '{password}'");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint data size: {fingerprintData.Length} bytes");
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Querying Officials table...");
            var official = await _dbContext.Officials
                .FirstOrDefaultAsync(o => o.Username == username && o.PasswordHash == password);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ [DatabaseService] No official found with username '{username}' and provided password");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Attempting to debug - checking all officials:");
                
                var allOfficials = await _dbContext.Officials.ToListAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Total officials in database: {allOfficials.Count}");
                
                foreach (var off in allOfficials)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService]   - Username: '{off.Username}' | PasswordHash: '{off.PasswordHash}' | Match: {off.Username == username && off.PasswordHash == password}");
                }
                
                return false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ [DatabaseService] Official found: {official.OfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Setting FingerPrintScan property...");
            
            official.FingerPrintScan = fingerprintData;
            _dbContext.Officials.Update(official);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Calling SaveChangesAsync()...");
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ [DatabaseService] Fingerprint updated for official {official.OfficialId} ({username}). Data size: {fingerprintData.Length} bytes");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ [DatabaseService] Error updating official fingerprint: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Exception type: {ex.GetType().FullName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Stack Trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<Voter?> GetVoterByIdAsync(Guid voterId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Querying Voters for VoterId: {voterId}");
            
            var voter = await _dbContext.Voters
                .FirstOrDefaultAsync(v => v.VoterId == voterId);

            if (voter == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No voter found with VoterId '{voterId}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found: {voter.VoterId}");
            Console.WriteLine($"    Name: {voter.FirstName} {voter.LastName}");
            Console.WriteLine($"    Electoral Roll: {voter.ElectoralRollNumber}");

            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error retrieving voter by ID: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }
}
