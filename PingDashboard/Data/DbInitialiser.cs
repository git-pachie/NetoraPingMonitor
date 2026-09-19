using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace PingDashboard.Data;

/// <summary>
/// Ensures the SQLite database and schema exist on every startup.
/// Self-heals if the DB file exists but the Clients table is missing,
/// and adds any newly-introduced columns to an existing table.
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
            db.Database.EnsureCreated();

            if (!ClientsTableExists(db))
            {
                log.LogWarning("Clients table missing despite EnsureCreated — recreating DB.");
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }

            // Add any columns introduced after the DB was first created
            EnsureColumn(db, "NotifyEmail", "TEXT");
            EnsureColumn(db, "IsNotificationEnabled", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(db, "NapboxName", "TEXT");

            // Create tables introduced after the DB was first created
            EnsureNapboxTable(db);
            EnsureStatusLogTable(db);

            log.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to initialise database. Recreating fresh.");
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }
    }

    private static bool ClientsTableExists(AppDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Clients';";
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        catch { return false; }
    }

    /// <summary>Adds a column to the Clients table if it doesn't already exist.</summary>
    private static void EnsureColumn(AppDbContext db, string columnName, string columnType)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        // Check existing columns via PRAGMA
        bool exists = false;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(Clients);";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                // column 1 of PRAGMA table_info is the column name
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE Clients ADD COLUMN {columnName} {columnType};";
            alter.ExecuteNonQuery();
        }
    }

    /// <summary>Creates the Napboxes table if it doesn't already exist.</summary>
    private static void EnsureNapboxTable(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS Napboxes (
                Id          INTEGER NOT NULL CONSTRAINT PK_Napboxes PRIMARY KEY AUTOINCREMENT,
                NapboxName  TEXT    NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Creates the StatusLogs table if it doesn't already exist.</summary>
    private static void EnsureStatusLogTable(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS StatusLogs (
                Id          INTEGER NOT NULL CONSTRAINT PK_StatusLogs PRIMARY KEY AUTOINCREMENT,
                ClientName  TEXT    NOT NULL,
                IpAddress   TEXT    NOT NULL,
                IsConnected INTEGER NOT NULL,
                Timestamp   TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_StatusLogs_Timestamp ON StatusLogs (Timestamp);";
        cmd.ExecuteNonQuery();
    }
}
