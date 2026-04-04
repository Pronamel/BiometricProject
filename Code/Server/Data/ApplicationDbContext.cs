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
                .HasIndex(v => v.County)
                .HasDatabaseName("idx_voters_county_hash");

            modelBuilder.Entity<Voter>()
                .HasIndex(v => v.WardId)
                .HasDatabaseName("idx_voters_ward_hash");

            modelBuilder.Entity<Voter>()
                .HasIndex(v => v.Sdi)
                .HasDatabaseName("idx_voters_sdi");

            modelBuilder.Entity<Voter>()
                .Property(v => v.Sdi)
                .HasColumnName("sdi");

            modelBuilder.Entity<Voter>()
                .Property(v => v.NationalId)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.ElectoralRollNumber)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.FirstName)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.LastName)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.DateOfBirth)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.AddressLine1)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.PreviousAddress)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.Postcode)
                .HasColumnType("bytea")
                .IsRequired(false);

            modelBuilder.Entity<Voter>()
                .Property(v => v.RegisteredDate)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.WardId)
                .HasColumnType("character(64)");

            modelBuilder.Entity<Voter>()
                .Property(v => v.County)
                .HasColumnType("character(64)")
                .HasMaxLength(64)
                .IsFixedLength();

            modelBuilder.Entity<Voter>()
                .Property(v => v.WrappedDek)
                .HasColumnType("bytea");

            modelBuilder.Entity<Voter>()
                .Property(v => v.KeyId)
                .HasColumnType("text");

            modelBuilder.Entity<Official>()
                .Property(o => o.FingerPrintScan)
                .HasColumnType("bytea");

            modelBuilder.Entity<Official>()
                .Property(o => o.KeyId)
                .HasColumnType("text");

            modelBuilder.Entity<Official>()
                .Property(o => o.WrappedDek)
                .HasColumnType("bytea");
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
