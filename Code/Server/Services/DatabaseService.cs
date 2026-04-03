using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<List<PollingStationDto>> GetAllPollingStationsAsync()
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Fetching all polling stations for dropdown");
            
            var pollingStations = await _dbContext.PollingStations
                .Include(ps => ps.Constituency)
                .ToListAsync();

            var pollingStationDtos = pollingStations.Select(ps => new PollingStationDto(
                ps.PollingStationId,
                ps.PollingStationCode ?? "Unknown",
                ps.County ?? "Unknown",
                ps.Constituency?.Name ?? "Unknown",
                $"{ps.PollingStationCode} - {ps.County} ({ps.Constituency?.Name})"
            )).ToList();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Found {pollingStationDtos.Count} polling stations");
            foreach (var ps in pollingStationDtos.Take(5))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - {ps.DisplayName}");
            }
            if (pollingStationDtos.Count > 5)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   ... and {pollingStationDtos.Count - 5} more");
            }

            return pollingStationDtos;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error retrieving polling stations: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<PollingStationDto>();
        }
    }

    public async Task<Official?> GetOfficialByCredentialsAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Querying Officials for username: {username}");
            
            var official = await _dbContext.Officials
                .Include(o => o.AssignedPollingStation)
                .ThenInclude(ps => ps!.Constituency)
                .FirstOrDefaultAsync(o => o.Username == username);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No official found with username '{username}'");
                return null;
            }

            if (!PasswordHasher.VerifyPassword(official.PasswordHash, password))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Invalid password for username '{username}'");
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Fingerprint data size: {fingerprintData.Length} bytes");
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Querying Officials table...");
            var official = await _dbContext.Officials
                .FirstOrDefaultAsync(o => o.Username == username);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ [DatabaseService] No official found with username '{username}'");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Attempting to debug - checking all officials:");
                
                var allOfficials = await _dbContext.Officials.ToListAsync();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Total officials in database: {allOfficials.Count}");
                
                foreach (var off in allOfficials)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService]   - Username: '{off.Username}' | UsernameMatch: {off.Username == username}");
                }
                
                return false;
            }

            if (!PasswordHasher.VerifyPassword(official.PasswordHash, password))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ [DatabaseService] Invalid password for username '{username}'");
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

    public async Task<(bool Success, string Message, Guid? VoterId)> CreateVoterAsync(
        string nationalInsuranceNumber,
        string firstName,
        string lastName,
        DateTime dateOfBirth,
        string addressLine1,
        string? previousAddress,
        string postCode,
        string county,
        string constituencyName,
        string sdi,
        byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(addressLine1) ||
                string.IsNullOrWhiteSpace(county) ||
                string.IsNullOrWhiteSpace(constituencyName) ||
                string.IsNullOrWhiteSpace(sdi) ||
                fingerprintData == null ||
                fingerprintData.Length == 0)
            {
                return (false, "Missing required fields", null);
            }

            var constituency = await _dbContext.Constituencies
                .FirstOrDefaultAsync(c => c.Name == constituencyName.Trim());

            if (constituency == null)
            {
                return (false, "Constituency name not found", null);
            }

            var voter = new Voter
            {
                NationalId = nationalInsuranceNumber.Trim(),
                Sdi = sdi,
                ConstituencyId = constituency.ConstituencyId,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                DateOfBirth = new DateTime(dateOfBirth.Year, dateOfBirth.Month, dateOfBirth.Day, 0, 0, 0, DateTimeKind.Utc),
                AddressLine1 = addressLine1.Trim(),
                PreviousAddress = string.IsNullOrWhiteSpace(previousAddress) ? "NA" : previousAddress.Trim(),
                Postcode = postCode.Trim(),
                FingerprintScan = fingerprintData,
                HasVoted = false,
                RegisteredDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0, DateTimeKind.Utc),
                County = county.Trim()
            };

            _dbContext.Voters.Add(voter);
            await _dbContext.SaveChangesAsync();

            return (true, "Voter created successfully", voter.VoterId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error creating voter: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return (false, "Failed to create voter", null);
        }
    }

    public async Task<(bool Success, string Message, Guid? OfficialId)> CreateOfficialAsync(
        string username,
        string passwordHash,
        Guid pollingStationId,
        byte[] fingerprintData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(passwordHash) ||
                fingerprintData == null ||
                fingerprintData.Length == 0)
            {
                return (false, "Missing required fields", null);
            }

            if (!passwordHash.StartsWith("$argon2", StringComparison.Ordinal))
            {
                return (false, "Password must be an Argon2 hash", null);
            }

            // Verify polling station exists
            var pollingStation = await _dbContext.PollingStations
                .FirstOrDefaultAsync(ps => ps.PollingStationId == pollingStationId);

            if (pollingStation == null)
            {
                return (false, "Polling station not found", null);
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔐 Using client-provided Argon2 password hash for official: {username}");

            var official = new Official
            {
                OfficialId = Guid.NewGuid(),
                Username = username.Trim(),
                PasswordHash = passwordHash,
                LastLogin = null,
                AssignedPollingStationId = pollingStationId,
                FingerPrintScan = fingerprintData
            };

            _dbContext.Officials.Add(official);
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Official created successfully: {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Official ID: {official.OfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Polling Station: {pollingStation.PollingStationCode} ({pollingStation.County})");

            return (true, "Official created successfully", official.OfficialId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error creating official: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return (false, "Failed to create official", null);
        }
    }

    public async Task<Voter?> GetVoterByNINAsync(string nin)
    {
        try
        {
            var normalizedNin = nin.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Looking up voter by NIN: {nin}");
            
            var voter = await _dbContext.Voters
                .Include(v => v.Constituency)
                .FirstOrDefaultAsync(v => 
                    v.NationalId
                        .Trim()
                        .ToUpper()
                        .Replace(" ", string.Empty) == normalizedNin);

            if (voter == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No voter found with NIN '{nin}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found by NIN: {voter.FirstName} {voter.LastName} (ID: {voter.VoterId})");
            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error looking up voter by NIN: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<Voter?> GetVoterByNameAndDateAsync(
        string firstName, string lastName, DateTime dateOfBirth)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Looking up voter by Name+DOB: {firstName} {lastName} ({dateOfBirth:yyyy-MM-dd})");
            
            var firstNameUpper = firstName.Trim().ToUpperInvariant();
            var lastNameUpper = lastName.Trim().ToUpperInvariant();
            var dobDate = dateOfBirth.Date;

            // Use SQL date casting to avoid timezone shifts in timestamptz DOB values.
            var firstNameParam = new NpgsqlParameter<string>("firstName", firstNameUpper);
            var lastNameParam = new NpgsqlParameter<string>("lastName", lastNameUpper);
            var dobParam = new NpgsqlParameter<DateTime>("dob", dobDate);

            var voter = await _dbContext.Voters
                .FromSqlRaw(
                    @"SELECT v.*
                      FROM public.""Voters"" v
                      WHERE UPPER(TRIM(v.""FirstName"")) = @firstName
                        AND UPPER(TRIM(v.""LastName"")) = @lastName
                        AND (v.""DateOfBirth"" AT TIME ZONE 'UTC')::date = @dob::date
                      LIMIT 1",
                    firstNameParam,
                    lastNameParam,
                    dobParam)
                .Include(v => v.Constituency)
                .FirstOrDefaultAsync();

            if (voter == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No voter found with Name '{firstName} {lastName}' DOB '{dateOfBirth:yyyy-MM-dd}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found by Name+DOB: {voter.FirstName} {voter.LastName} (ID: {voter.VoterId})");
            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error looking up voter by Name+DOB: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<Voter?> GetVoterBySdiAsync(string sdi)
    {
        try
        {
            var normalizedSdi = sdi.Trim().ToLowerInvariant();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Looking up voter by SDI");

            var matches = await _dbContext.Voters
                .Include(v => v.Constituency)
                .Where(v => v.Sdi != null && v.Sdi == normalizedSdi)
                .Take(2)
                .ToListAsync();

            if (matches.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ No voter found with provided SDI");
                return null;
            }

            if (matches.Count > 1)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  Multiple voters matched same SDI; using first match for now");
            }

            var voter = matches[0];
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found by SDI: {voter.FirstName} {voter.LastName} (ID: {voter.VoterId})");
            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error looking up voter by SDI: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }
}

// DTO for polling stations response
public record PollingStationDto(
    Guid PollingStationId,
    string Code,
    string County,
    string Constituency,
    string DisplayName
);
