using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace PingDashboard.Data;

/// <summary>
/// Ensures the SQLite database and schema exist on every startup.
/// Self-heals if the DB file exists but the Clients table is missing
/// (e.g. a stale file from a previous failed migration attempt).
/// </summary>
public static class DbInitialiser
{
    public static void Initialise(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            // First attempt: create schema from model if DB is new
            db.Database.EnsureCreated();

            // Verify the Clients table actually exists
            if (!ClientsTableExists(db))
            {
                log.LogWarning("Clients table missing despite EnsureCreated — deleting and recreating DB.");
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }

            log.LogInformation("Database ready. Clients table confirmed.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to initialise database. Attempting fresh recreation.");
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }
    }

    private static bool ClientsTableExists(AppDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Clients';";
            var result = cmd.ExecuteScalar();
            return Convert.ToInt64(result) > 0;
        }
        catch
        {
            return false;
        }
    }
}
