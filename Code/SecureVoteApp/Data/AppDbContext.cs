using Microsoft.EntityFrameworkCore;
using SecureVoteApp.Models.Entities;

namespace SecureVoteApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Constituency> Constituencies { get; set; }
        public DbSet<Voter> Voters { get; set; }
        public DbSet<Election> Elections { get; set; }
        public DbSet<Candidate> Candidates { get; set; }
        public DbSet<PollingStation> PollingStations { get; set; }
        public DbSet<Official> Officials { get; set; }
        public DbSet<VoteRecord> VoteRecords { get; set; }
        public DbSet<ConstituencyResult> ConstituencyResults { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Candidate>()
                .HasIndex(c => new { c.ConstituencyId, c.ElectionId, c.FirstName, c.LastName })
                .IsUnique();

            modelBuilder.Entity<ConstituencyResult>()
                .HasIndex(r => new { r.ConstituencyId, r.CandidateId, r.ElectionId })
                .IsUnique();

            // Add other relationships as needed
        }
    }
}
