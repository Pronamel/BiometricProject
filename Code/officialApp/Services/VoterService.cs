using System;
using System.Threading.Tasks;
using officialApp.Data;
using officialApp.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace officialApp.Services
{
    public class VoterService
    {
        private readonly AppDbContext _db;

        public VoterService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Voter> RegisterVoterAsync(Voter voter)
        {
            _db.Voters.Add(voter);
            await _db.SaveChangesAsync();
            return voter;
        }

        public async Task<bool> MarkVotedAsync(Guid voterId)
        {
            var voter = await _db.Voters.FindAsync(voterId);
            if (voter == null) return false;
            voter.HasVoted = true;
            await _db.SaveChangesAsync();
            return true;
        }
        public async Task<List<Voter>> GetAllVotersAsync()
        {
            return await _db.Voters.ToListAsync();
        }
    }
}
