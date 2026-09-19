using Microsoft.EntityFrameworkCore;
using PingDashboard.Models;

namespace PingDashboard.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.IpAddress).IsUnique();
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.IpAddress).IsRequired().HasMaxLength(100);
        });
    }
}
