# insert-napboxes.ps1
# Inserts NAP boxes into the PingDashboard SQLite database on Windows.
# Idempotent: skips names that already exist. Requires the sqlite3.exe CLI on PATH,
# OR falls back to the Microsoft.Data.Sqlite assembly bundled with the app.
#
# Usage:
#   .\insert-napboxes.ps1 -DbPath "C:\path\to\pingdashboard.db"
#
# IMPORTANT: stop the running PingDashboard first so the DB isn't locked.

param(
    [string]$DbPath = "C:\Users\pachie\Desktop\Kiro\publish\PingDashboard\pingdashboard.db"
)

$ErrorActionPreference = 'Stop'

$names = @(
    'ALJOHN','ANDREI','ARABELA','BIMBO','BRGYHALL1','ROBERT','CHICELLE',
    'COMPOUND','FLORANTE','IGLESIA','ITLUGAN','JACKYBAYAN','JHAY','OGIE',
    'EXTENSION','SAPA1','SAPA2','SWEETIE'
) | Select-Object -Unique

if (-not (Test-Path $DbPath)) {
    Write-Error "Database not found: $DbPath"
    exit 1
}

$sqlitePath = (Get-Command sqlite3.exe -ErrorAction SilentlyContinue)?.Source

if ($sqlitePath) {
    foreach ($n in $names) {
        $safe = $n.Replace("'", "''")
        $sql = "INSERT INTO Napboxes (NapboxName) SELECT '$safe' WHERE NOT EXISTS (SELECT 1 FROM Napboxes WHERE NapboxName='$safe');"
        & $sqlitePath $DbPath $sql
    }
    Write-Host "Inserted napboxes via sqlite3.exe. Current list:"
    & $sqlitePath $DbPath "SELECT Id, NapboxName FROM Napboxes ORDER BY NapboxName;"
}
else {
    # Fallback: use Microsoft.Data.Sqlite from the published app folder.
    $dll = Get-ChildItem -Path "C:\Users\pachie\Desktop\Kiro\publish\PingDashboard" -Filter "Microsoft.Data.Sqlite.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dll) {
        Write-Error "sqlite3.exe not found and Microsoft.Data.Sqlite.dll not located. Install sqlite3 or run the .sql file manually."
        exit 1
    }
    Add-Type -Path $dll.FullName
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$DbPath")
    $conn.Open()
    foreach ($n in $names) {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "INSERT INTO Napboxes (NapboxName) SELECT @n WHERE NOT EXISTS (SELECT 1 FROM Napboxes WHERE NapboxName=@n);"
        [void]$cmd.Parameters.AddWithValue("@n", $n)
        [void]$cmd.ExecuteNonQuery()
    }
    $read = $conn.CreateCommand()
    $read.CommandText = "SELECT Id, NapboxName FROM Napboxes ORDER BY NapboxName;"
    $r = $read.ExecuteReader()
    while ($r.Read()) { "{0}`t{1}" -f $r.GetInt32(0), $r.GetString(1) | Write-Host }
    $conn.Close()
}

Write-Host "Done."
