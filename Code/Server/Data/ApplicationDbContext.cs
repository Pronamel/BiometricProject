using Microsoft.EntityFrameworkCore;
using Server.Models.Entities;

namespace Server.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Voter>()
                .Property(v => v.Sdi)
                .HasColumnName("sdi");
        }

        public DbSet<Candidate> Candidates { get; set; }
        public DbSet<Constituency> Constituencies { get; set; }
        public DbSet<ConstituencyResult> ConstituencyResults { get; set; }
        public DbSet<Election> Elections { get; set; }
        public DbSet<Official> Officials { get; set; }
        public DbSet<PollingStation> PollingStations { get; set; }
        public DbSet<Voter> Voters { get; set; }
        public DbSet<VoteRecord> VoteRecords { get; set; }
    }
}
