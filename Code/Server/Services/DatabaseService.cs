using System;
using System.Collections.Generic;
using System.Globalization;
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

    public async Task<bool> UpdateOfficialFingerprintAsync(string username, string password, string keyId, string wrappedDekBase64, string encryptedFingerPrintScan)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 [DatabaseService] UpdateOfficialFingerprintAsync called");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Username: '{username}'");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Encrypted fingerprint payload received");

            if (string.IsNullOrWhiteSpace(keyId) ||
                string.IsNullOrWhiteSpace(wrappedDekBase64) ||
                string.IsNullOrWhiteSpace(encryptedFingerPrintScan))
            {
                return false;
            }

            byte[] wrappedDek;
            byte[] encryptedFingerprintBytes;

            try
            {
                wrappedDek = Convert.FromBase64String(wrappedDekBase64.Trim());
                encryptedFingerprintBytes = Convert.FromBase64String(encryptedFingerPrintScan.Trim());
            }
            catch (FormatException)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ [DatabaseService] Invalid base64 in encrypted fingerprint payload");
                return false;
            }
            
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Setting encrypted fingerprint properties...");
            
            official.FingerPrintScan = encryptedFingerprintBytes;
            official.KeyId = keyId.Trim();
            official.WrappedDek = wrappedDek;
            _dbContext.Officials.Update(official);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Calling SaveChangesAsync()...");
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ [DatabaseService] Encrypted fingerprint updated for official {official.OfficialId} ({username}).");
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
            Console.WriteLine($"    Encrypted voter record loaded");

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
        string countyHash,
        string constituencyName,
        string sdi,
        string constituencyHash,
        string keyId,
        string wrappedDekBase64,
        string encryptedNationalInsuranceNumber,
        string encryptedFirstName,
        string encryptedLastName,
        string encryptedDateOfBirth,
        string encryptedAddressLine1,
        string encryptedAddressLine2,
        string encryptedPostCode,
        string encryptedFingerPrintScan)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(countyHash) ||
                string.IsNullOrWhiteSpace(constituencyName) ||
                string.IsNullOrWhiteSpace(sdi) ||
                string.IsNullOrWhiteSpace(constituencyHash) ||
                string.IsNullOrWhiteSpace(keyId) ||
                string.IsNullOrWhiteSpace(wrappedDekBase64) ||
                string.IsNullOrWhiteSpace(encryptedNationalInsuranceNumber) ||
                string.IsNullOrWhiteSpace(encryptedFirstName) ||
                string.IsNullOrWhiteSpace(encryptedLastName) ||
                string.IsNullOrWhiteSpace(encryptedDateOfBirth) ||
                string.IsNullOrWhiteSpace(encryptedAddressLine1) ||
                string.IsNullOrWhiteSpace(encryptedAddressLine2) ||
                string.IsNullOrWhiteSpace(encryptedPostCode) ||
                string.IsNullOrWhiteSpace(encryptedFingerPrintScan))
            {
                return (false, "Missing required fields", null);
            }

            var constituency = await _dbContext.Constituencies
                .FirstOrDefaultAsync(c => c.Name == constituencyName.Trim());

            if (constituency == null)
            {
                return (false, "Constituency name not found", null);
            }

            byte[] wrappedDek;
            byte[] encryptedNationalIdBytes;
            byte[] encryptedFirstNameBytes;
            byte[] encryptedLastNameBytes;
            byte[] encryptedDateOfBirthBytes;
            byte[] encryptedAddressLine1Bytes;
            byte[] encryptedAddressLine2Bytes;
            byte[] encryptedPostCodeBytes;
            byte[] encryptedFingerprintBytes;

            try
            {
                wrappedDek = Convert.FromBase64String(wrappedDekBase64.Trim());
                encryptedNationalIdBytes = Convert.FromBase64String(encryptedNationalInsuranceNumber.Trim());
                encryptedFirstNameBytes = Convert.FromBase64String(encryptedFirstName.Trim());
                encryptedLastNameBytes = Convert.FromBase64String(encryptedLastName.Trim());
                encryptedDateOfBirthBytes = Convert.FromBase64String(encryptedDateOfBirth.Trim());
                encryptedAddressLine1Bytes = Convert.FromBase64String(encryptedAddressLine1.Trim());
                encryptedAddressLine2Bytes = Convert.FromBase64String(encryptedAddressLine2.Trim());
                encryptedPostCodeBytes = Convert.FromBase64String(encryptedPostCode.Trim());
                encryptedFingerprintBytes = Convert.FromBase64String(encryptedFingerPrintScan.Trim());
            }
            catch (FormatException)
            {
                return (false, "Encrypted payload contains invalid base64", null);
            }

            var voter = new Voter
            {
                NationalId = encryptedNationalIdBytes,
                ElectoralRollNumber = Array.Empty<byte>(),
                Sdi = sdi,
                ConstituencyId = constituency.ConstituencyId,
                WardId = constituencyHash.Trim().ToLowerInvariant(),
                FirstName = encryptedFirstNameBytes,
                LastName = encryptedLastNameBytes,
                DateOfBirth = encryptedDateOfBirthBytes,
                AddressLine1 = encryptedAddressLine1Bytes,
                PreviousAddress = encryptedAddressLine2Bytes,
                Postcode = encryptedPostCodeBytes,
                FingerprintScan = encryptedFingerprintBytes,
                HasVoted = false,
                RegisteredDate = System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                County = countyHash.Trim().ToLowerInvariant(),
                KeyId = keyId.Trim(),
                WrappedDek = wrappedDek
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
        string keyId,
        string wrappedDekBase64,
        string encryptedFingerPrintScan)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(passwordHash) ||
                string.IsNullOrWhiteSpace(keyId) ||
                string.IsNullOrWhiteSpace(wrappedDekBase64) ||
                string.IsNullOrWhiteSpace(encryptedFingerPrintScan))
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

            byte[] wrappedDek;
            byte[] encryptedFingerprintBytes;

            try
            {
                wrappedDek = Convert.FromBase64String(wrappedDekBase64.Trim());
                encryptedFingerprintBytes = Convert.FromBase64String(encryptedFingerPrintScan.Trim());
            }
            catch (FormatException)
            {
                return (false, "Encrypted fingerprint payload contains invalid base64", null);
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔐 Using client-provided Argon2 password hash for official: {username}");

            var official = new Official
            {
                OfficialId = Guid.NewGuid(),
                Username = username.Trim(),
                PasswordHash = passwordHash,
                LastLogin = null,
                AssignedPollingStationId = pollingStationId,
                FingerPrintScan = encryptedFingerprintBytes,
                KeyId = keyId.Trim(),
                WrappedDek = wrappedDek
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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ NIN lookup disabled: NationalId is stored encrypted");
        return null;
    }

    public async Task<Voter?> GetVoterByNameAndDateAsync(
        string firstName, string lastName, DateTime dateOfBirth)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ Name+DOB lookup disabled: identity fields are stored encrypted");
        return null;
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Voter found by SDI: ID={voter.VoterId}");
            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error looking up voter by SDI: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<List<CandidateDto>> GetCandidatesByElectionIdAsync(Guid electionId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔍 Fetching candidates for election ID: {electionId}");
            
            var candidates = await _dbContext.Candidates
                .Where(c => c.ElectionId == electionId)
                .ToListAsync();

            if (candidates.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  No candidates found for election ID: {electionId}");
                return new List<CandidateDto>();
            }

            var candidateDtos = candidates.Select(c => new CandidateDto(
                c.CandidateId,
                c.FirstName,
                c.LastName,
                c.Party ?? "Independent",
                c.Bio ?? string.Empty
            )).ToList();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Found {candidateDtos.Count} candidates for election");
            foreach (var candidate in candidateDtos.Take(5))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   - {candidate.FirstName} {candidate.LastName} ({candidate.Party})");
            }
            if (candidateDtos.Count > 5)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   ... and {candidateDtos.Count - 5} more");
            }

            return candidateDtos;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ Error fetching candidates by election ID: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<CandidateDto>();
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

// DTO for candidates response
public record CandidateDto(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Party,
    string Bio
);
