using Microsoft.EntityFrameworkCore;
using PingDashboard.Models;

namespace PingDashboard.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Napbox> Napboxes => Set<Napbox>();
    public DbSet<StatusLog> StatusLogs => Set<StatusLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.IpAddress).IsUnique();
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.IpAddress).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Napbox>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.NapboxName).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<StatusLog>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.ClientName).IsRequired().HasMaxLength(200);
            e.Property(s => s.IpAddress).IsRequired().HasMaxLength(100);
            e.HasIndex(s => s.Timestamp);
        });
    }
}
