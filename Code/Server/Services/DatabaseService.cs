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

    public async Task<int> GetVoteRecordsCountByPollingStationAsync(Guid pollingStationId)
    {
        return await _dbContext.VoteRecords
            .Where(vr => vr.PollingStationId == pollingStationId)
            .CountAsync();
    }

    public async Task<int> GetExpectedVotesByPollingStationAsync(Guid pollingStationId)
    {
        return await _dbContext.PollingStations
            .Where(ps => ps.PollingStationId == pollingStationId)
            .Select(ps => ps.ExpectedVotes)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PollingStationDto>> GetAllPollingStationsAsync()
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching all polling stations for dropdown");
            
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

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found {pollingStationDtos.Count} polling stations");
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error retrieving polling stations: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<PollingStationDto>();
        }
    }

    public async Task<Official?> GetOfficialByCredentialsAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Querying Officials for username: {username}");
            
            var official = await _dbContext.Officials
                .Include(o => o.AssignedPollingStation)
                .ThenInclude(ps => ps!.Constituency)
                .FirstOrDefaultAsync(o => o.Username == username);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No official found with username '{username}'");
                return null;
            }

            if (!PasswordHasher.VerifyPassword(official.PasswordHash, password))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Invalid password for username '{username}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official found: {official.OfficialId}");
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error retrieving official by credentials: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<bool> UpdateOfficialFingerprintAsync(string username, string password, string keyId, string wrappedDekBase64, string encryptedFingerPrintScan)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] UpdateOfficialFingerprintAsync called");
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
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Invalid base64 in encrypted fingerprint payload");
                return false;
            }
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Querying Officials table...");
            var official = await _dbContext.Officials
                .FirstOrDefaultAsync(o => o.Username == username);

            if (official == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] No official found with username '{username}'");
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
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Invalid password for username '{username}'");
                return false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Official found: {official.OfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Setting encrypted fingerprint properties...");
            
            official.FingerPrintScan = encryptedFingerprintBytes;
            official.KeyId = keyId.Trim();
            official.WrappedDek = wrappedDek;
            _dbContext.Officials.Update(official);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Calling SaveChangesAsync()...");
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Encrypted fingerprint updated for official {official.OfficialId} ({username}).");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Error updating official fingerprint: {ex.Message}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Exception type: {ex.GetType().FullName}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DatabaseService] Stack Trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<Voter?> GetVoterByIdAsync(Guid voterId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Querying Voters for VoterId: {voterId}");
            
            var voter = await _dbContext.Voters
                .FirstOrDefaultAsync(v => v.VoterId == voterId);

            if (voter == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No voter found with VoterId '{voterId}'");
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Voter found: {voter.VoterId}");
            Console.WriteLine($"    Encrypted voter record loaded");

            return voter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error retrieving voter by ID: {ex.Message}");
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
        string encryptedTownOfBirth,
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
                string.IsNullOrWhiteSpace(encryptedTownOfBirth) ||
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

            // SDI collisions are allowed (e.g., twins/triplets with identical identity fields).
            var existingSdiCount = await _dbContext.Voters
                .CountAsync(v => v.Sdi == sdi);
            if (existingSdiCount > 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Creating voter with non-unique SDI. Existing records: {existingSdiCount}");
            }

            byte[] wrappedDek;
            byte[] encryptedNationalIdBytes;
            byte[] encryptedFirstNameBytes;
            byte[] encryptedLastNameBytes;
            byte[] encryptedDateOfBirthBytes;
            byte[] encryptedTownOfBirthBytes;
            byte[] encryptedPostCodeBytes;
            byte[] encryptedFingerprintBytes;

            try
            {
                wrappedDek = Convert.FromBase64String(wrappedDekBase64.Trim());
                encryptedNationalIdBytes = Convert.FromBase64String(encryptedNationalInsuranceNumber.Trim());
                encryptedFirstNameBytes = Convert.FromBase64String(encryptedFirstName.Trim());
                encryptedLastNameBytes = Convert.FromBase64String(encryptedLastName.Trim());
                encryptedDateOfBirthBytes = Convert.FromBase64String(encryptedDateOfBirth.Trim());
                encryptedTownOfBirthBytes = Convert.FromBase64String(encryptedTownOfBirth.Trim());
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
                Sdi = sdi,
                ConstituencyId = constituency.ConstituencyId,
                WardId = constituencyHash.Trim().ToLowerInvariant(),
                FirstName = encryptedFirstNameBytes,
                LastName = encryptedLastNameBytes,
                DateOfBirth = encryptedDateOfBirthBytes,
                TownOfBirth = encryptedTownOfBirthBytes,
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error creating voter: {ex.Message}");
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

            // Check if username already exists
            var existingOfficial = await _dbContext.Officials
                .FirstOrDefaultAsync(o => o.Username == username.Trim());

            if (existingOfficial != null)
            {
                return (false, "An official with this username has already been created", null);
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

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using client-provided Argon2 password hash for official: {username}");

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

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Official created successfully: {username}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Official ID: {official.OfficialId}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   Polling Station: {pollingStation.PollingStationCode} ({pollingStation.County})");

            return (true, "Official created successfully", official.OfficialId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error creating official: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return (false, "Failed to create official", null);
        }
    }

    public async Task<Voter?> GetVoterByNINAsync(string nin)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: NIN lookup disabled: NationalId is stored encrypted");
        return null;
    }

    public async Task<Voter?> GetVoterByNameAndDateAsync(
        string firstName, string lastName, DateTime dateOfBirth)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Name+DOB lookup disabled: identity fields are stored encrypted");
        return null;
    }

    public async Task<Voter?> GetVoterBySdiAsync(string sdi)
    {
        try
        {
            var matches = await GetVotersBySdiAsync(sdi, limit: 2);
            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Multiple voters matched same SDI; returning first match for compatibility");
            }

            return matches[0];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error looking up voter by SDI: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<List<Voter>> GetVotersBySdiAsync(string sdi, int limit = 10)
    {
        try
        {
            var normalizedSdi = sdi.Trim().ToLowerInvariant();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Looking up voters by SDI");

            var safeLimit = Math.Clamp(limit, 1, 50);
            var matches = await _dbContext.Voters
                .Include(v => v.Constituency)
                .Where(v => v.Sdi != null && v.Sdi == normalizedSdi)
                .OrderBy(v => v.RegisteredDate)
                .ThenBy(v => v.VoterId)
                .Take(safeLimit)
                .ToListAsync();

            if (matches.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No voter found with provided SDI");
                return new List<Voter>();
            }

            if (matches.Count > 1)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: SDI collision detected. Candidate count: {matches.Count}");
            }

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error looking up voters by SDI: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<Voter>();
        }
    }

    public async Task<List<Voter>> GetVotersByIdsAsync(IEnumerable<Guid> voterIds)
    {
        try
        {
            var ids = voterIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new List<Voter>();
            }

            var voters = await _dbContext.Voters
                .Where(v => ids.Contains(v.VoterId))
                .ToListAsync();

            return voters;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error looking up voters by ID list: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<Voter>();
        }
    }

    public async Task<(bool Success, string Message)> AssignProxyToVoterAsync(Guid representedVoterId, string proxySdi)
    {
        try
        {
            if (representedVoterId == Guid.Empty || string.IsNullOrWhiteSpace(proxySdi))
            {
                return (false, "Represented voter and proxy identity are required");
            }

            var normalizedProxySdi = proxySdi.Trim().ToLowerInvariant();
            var representedVoter = await _dbContext.Voters
                .FirstOrDefaultAsync(v => v.VoterId == representedVoterId);

            if (representedVoter == null)
            {
                return (false, "Represented voter not found");
            }

            representedVoter.ProxySdi = normalizedProxySdi;
            _dbContext.Voters.Update(representedVoter);
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Proxy SDI assigned for voter {representedVoter.VoterId}");
            return (true, "Proxy voter assigned successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error assigning proxy voter: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return (false, "Failed to assign proxy voter");
        }
    }

    public async Task<List<CandidateDto>> GetCandidatesByElectionIdAsync(Guid electionId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching candidates for election ID: {electionId}");
            
            var candidates = await _dbContext.Candidates
                .Where(c => c.ElectionId == electionId)
                .ToListAsync();

            if (candidates.Count == 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: No candidates found for election ID: {electionId}");
                return new List<CandidateDto>();
            }

            var candidateDtos = candidates.Select(c => new CandidateDto(
                c.CandidateId,
                c.FirstName,
                c.LastName,
                c.Party ?? "Independent",
                c.Bio ?? string.Empty
            )).ToList();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found {candidateDtos.Count} candidates for election");
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching candidates by election ID: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return new List<CandidateDto>();
        }
    }

    // ==========================================
    // ELECTION STATISTICS METHODS
    // ==========================================

    public async Task<(bool Success, Guid? CurrentElectionId, int TotalVotes, int InvalidVotes, int ExpectedVotes)> GetElectionStatisticsAsync(Guid pollingStationId, Guid? electionId = null)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching election statistics for polling station {pollingStationId}");

            Election? currentElection;

            if (electionId.HasValue)
            {
                currentElection = await _dbContext.Elections
                    .FirstOrDefaultAsync(e => e.ElectionId == electionId.Value);

                if (currentElection == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Requested election not found: {electionId.Value}");
                    return (false, null, 0, 0, 0);
                }
            }
            else
            {
                currentElection = await _dbContext.Elections
                    .Where(e => e.Status == "Active" || e.Status == "OnGoing")
                    .OrderByDescending(e => e.ElectionDate)
                    .FirstOrDefaultAsync();

                // Fallback to most recent election so dashboard can still render if status is stale.
                if (currentElection == null)
                {
                    currentElection = await _dbContext.Elections
                        .OrderByDescending(e => e.ElectionDate)
                        .FirstOrDefaultAsync();
                }
            }

            if (currentElection == null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: No election found");
                return (false, null, 0, 0, 0);
            }

            var totalVotes = await _dbContext.VoteRecords
                .Where(vr => vr.PollingStationId == pollingStationId && vr.ElectionId == currentElection.ElectionId)
                .CountAsync();

            var pollingStation = await _dbContext.PollingStations
                .FirstOrDefaultAsync(ps => ps.PollingStationId == pollingStationId);

            var invalidVotes = pollingStation?.InvalidVotes ?? 0;
            var expectedVotes = pollingStation?.ExpectedVotes ?? 0;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Election statistics: Total={totalVotes}, Invalid={invalidVotes}, Expected={expectedVotes}");
            return (true, currentElection.ElectionId, totalVotes, invalidVotes, expectedVotes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching election statistics: {ex.Message}");
            return (false, null, 0, 0, 0);
        }
    }

    public async Task<List<CandidateVoteDto>> GetVotesByCandidate(Guid electionId, Guid? pollingStationId = null)
    {
        try
        {
            if (pollingStationId.HasValue)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching votes by candidate for polling station {pollingStationId.Value}");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching election-wide votes by candidate for election {electionId}");
            }

            var voteRecordsQuery = _dbContext.VoteRecords
                .Where(vr => vr.ElectionId == electionId && vr.CandidateId != null);

            if (pollingStationId.HasValue)
            {
                voteRecordsQuery = voteRecordsQuery.Where(vr => vr.PollingStationId == pollingStationId.Value);
            }

            var groupedVotes = await voteRecordsQuery
                .Where(vr => vr.CandidateId.HasValue)
                .GroupBy(vr => vr.CandidateId!.Value)
                .Select(g => new
                {
                    CandidateId = g.Key,
                    VoteCount = g.Count()
                })
                .OrderByDescending(g => g.VoteCount)
                .ToListAsync();

            var candidateIds = groupedVotes
                .Select(g => g.CandidateId)
                .ToList();

            var candidateLookup = await _dbContext.Candidates
                .Where(c => candidateIds.Contains(c.CandidateId))
                .Select(c => new
                {
                    c.CandidateId,
                    c.FirstName,
                    c.LastName,
                    c.Party
                })
                .ToDictionaryAsync(c => c.CandidateId, c => c);

            var votesByCandidate = groupedVotes
                .Select(v =>
                {
                    candidateLookup.TryGetValue(v.CandidateId, out var candidate);
                    return new CandidateVoteDto(
                        v.CandidateId,
                        candidate?.FirstName ?? "Unknown",
                        candidate?.LastName ?? "Candidate",
                        candidate?.Party ?? "Independent",
                        v.VoteCount
                    );
                })
                .OrderByDescending(cv => cv.VoteCount)
                .ToList();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found votes for {votesByCandidate.Count} candidates");
            return votesByCandidate;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching votes by candidate: {ex.Message}");
            return new List<CandidateVoteDto>();
        }
    }

    public async Task<List<PollingStationStatsDto>> GetPollingStationStats(Guid constitutionId, Guid electionId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching polling station stats for constituency {constitutionId}");

            var stats = await _dbContext.PollingStations
                .Where(ps => ps.ConstituencyId == constitutionId)
                .Select(ps => new PollingStationStatsDto(
                    ps.PollingStationId,
                    ps.PollingStationCode ?? "",
                    _dbContext.VoteRecords
                        .Where(vr => vr.PollingStationId == ps.PollingStationId && vr.ElectionId == electionId)
                        .Count(),
                    ps.InvalidVotes,
                    ps.ExpectedVotes
                ))
                .ToListAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found stats for {stats.Count} polling stations");
            return stats;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching polling station stats: {ex.Message}");
            return new List<PollingStationStatsDto>();
        }
    }

    public async Task<List<ConstituencyStatsDto>> GetConstituencyStats(Guid electionId)
    {
        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Fetching constituency stats for election {electionId}");

            var constituencies = await _dbContext.Constituencies
                .Select(c => new
                {
                    c.ConstituencyId,
                    Name = c.Name ?? ""
                })
                .ToListAsync();

            var votesByConstituency = await _dbContext.VoteRecords
                .Where(vr => vr.ElectionId == electionId && vr.ConstituencyId.HasValue)
                .GroupBy(vr => vr.ConstituencyId!.Value)
                .Select(g => new
                {
                    ConstituencyId = g.Key,
                    TotalVotes = g.Count()
                })
                .ToDictionaryAsync(x => x.ConstituencyId, x => x.TotalVotes);

            var expectedByConstituency = await _dbContext.PollingStations
                .GroupBy(ps => ps.ConstituencyId)
                .Select(g => new
                {
                    ConstituencyId = g.Key,
                    ExpectedVotes = g.Sum(ps => ps.ExpectedVotes)
                })
                .ToDictionaryAsync(x => x.ConstituencyId, x => x.ExpectedVotes);

            var stats = constituencies
                .Select(c =>
                {
                    votesByConstituency.TryGetValue(c.ConstituencyId, out var totalVotes);
                    expectedByConstituency.TryGetValue(c.ConstituencyId, out var expectedVotes);
                    return new ConstituencyStatsDto(
                        c.ConstituencyId,
                        c.Name,
                        totalVotes,
                        expectedVotes
                    );
                })
                .Where(s => s.ExpectedVotes > 0)
                .OrderByDescending(s => s.TotalVotes)
                .ToList();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found stats for {stats.Count} constituencies");
            return stats;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error fetching constituency stats: {ex.Message}");
            return new List<ConstituencyStatsDto>();
        }
    }

    public async Task<int> GetCountryPopulationAsync()
    {
        await Task.CompletedTask;
        // Great Britain population baseline for national participation metric.
        return 67_700_000;
    }

    public async Task<int> GetRegisteredVotersCountAsync()
    {
        return await _dbContext.Voters.CountAsync();
    }

    public async Task<int> GetTotalPollingStationsCountAsync()
    {
        return await _dbContext.PollingStations.CountAsync();
    }

    public async Task<int> GetInvalidVotesByElectionAsync(Guid electionId)
    {
        return await _dbContext.VoteRecords
            .Where(vr => vr.ElectionId == electionId && vr.CandidateId == null)
            .CountAsync();
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

// DTO for candidate votes
public record CandidateVoteDto(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Party,
    int VoteCount
);

// DTO for polling station statistics
public record PollingStationStatsDto(
    Guid PollingStationId,
    string Code,
    int TotalVotes,
    int InvalidVotes,
    int ExpectedVotes
);

// DTO for constituency statistics
public record ConstituencyStatsDto(
    Guid ConstituencyId,
    string ConstituencyName,
    int TotalVotes,
    int ExpectedVotes
);
