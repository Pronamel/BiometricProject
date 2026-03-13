using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using officialApp.Data;
using System.IO;

namespace officialApp.Services
{
    public static class DbContextFactory
    {
        public static AppDbContext Create()
        {
            // Read appsettings.json for connection string
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var connectionString = config.GetConnectionString("DefaultConnection");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
