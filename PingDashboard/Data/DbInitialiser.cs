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
            EnsureUsersTable(db);

            // New user profile columns (added after Users table existed)
            EnsureColumn(db, "Users", "Email", "TEXT");
            EnsureColumn(db, "Users", "Mobile", "TEXT");
            EnsureColumn(db, "Users", "ProfileImagePath", "TEXT");

            EnsureUserNapboxTable(db);
            SeedDefaultAdmin(db, log);

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
        => EnsureColumn(db, "Clients", columnName, columnType);

    /// <summary>Adds a column to the given table if it doesn't already exist.</summary>
    private static void EnsureColumn(AppDbContext db, string tableName, string columnName, string columnType)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        bool exists = false;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
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
            alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
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

    /// <summary>Creates the Users table if it doesn't already exist.</summary>
    private static void EnsureUsersTable(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS Users (
                Id           INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                Username     TEXT    NOT NULL,
                PasswordHash TEXT    NOT NULL,
                PasswordSalt TEXT    NOT NULL,
                Role         TEXT    NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users (Username);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Creates the UserNapboxes join table if it doesn't already exist.</summary>
    private static void EnsureUserNapboxTable(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS UserNapboxes (
                Id         INTEGER NOT NULL CONSTRAINT PK_UserNapboxes PRIMARY KEY AUTOINCREMENT,
                UserId     INTEGER NOT NULL,
                NapboxName TEXT    NOT NULL,
                CONSTRAINT FK_UserNapboxes_Users FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_UserNapboxes_UserId ON UserNapboxes (UserId);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Seeds a default Admin account on a fresh database so someone can log in.
    /// Default credentials: admin / admin123  (change immediately after first login).
    /// </summary>
    private static void SeedDefaultAdmin(AppDbContext db, ILogger log)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        long userCount;
        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM Users;";
            userCount = Convert.ToInt64(count.ExecuteScalar());
        }

        if (userCount > 0) return;

        var (hash, salt) = Services.PasswordHasher.Hash("admin123");
        using var insert = conn.CreateCommand();
        insert.CommandText =
            "INSERT INTO Users (Username, PasswordHash, PasswordSalt, Role) " +
            "VALUES ($u, $h, $s, $r);";
        AddParam(insert, "$u", "admin");
        AddParam(insert, "$h", hash);
        AddParam(insert, "$s", salt);
        AddParam(insert, "$r", Models.Roles.Admin);
        insert.ExecuteNonQuery();

        log.LogWarning("Seeded default admin account (admin / admin123). Change the password after first login.");
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
