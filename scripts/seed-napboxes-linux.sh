#!/usr/bin/env bash
# seed-napboxes-linux.sh
# Self-contained: inserts NAP boxes into the PingDashboard SQLite DB on Linux.
# Idempotent — safe to run multiple times (skips names that already exist).
#
# Usage:
#   chmod +x seed-napboxes-linux.sh
#   ./seed-napboxes-linux.sh                                  # uses default DB path
#   ./seed-napboxes-linux.sh ~/PingDashboardApp/pingdashboard.db
#
# Requires sqlite3:  sudo apt-get update && sudo apt-get install -y sqlite3
# Stop the app first so the DB isn't locked:  pkill -9 -f PingDashboard

set -euo pipefail

DB_PATH="${1:-$HOME/PingDashboardApp/pingdashboard.db}"

# Unique napbox names (duplicates from the source list already removed).
NAPBOXES=(
    ALJOHN
    ANDREI
    ARABELA
    BIMBO
    BRGYHALL1
    ROBERT
    CHICELLE
    COMPOUND
    FLORANTE
    IGLESIA
    ITLUGAN
    JACKYBAYAN
    JHAY
    OGIE
    EXTENSION
    SAPA1
    SAPA2
    SWEETIE
)

if ! command -v sqlite3 >/dev/null 2>&1; then
    echo "ERROR: sqlite3 is not installed. Run:" >&2
    echo "  sudo apt-get update && sudo apt-get install -y sqlite3" >&2
    exit 1
fi

if [[ ! -f "$DB_PATH" ]]; then
    echo "ERROR: database not found: $DB_PATH" >&2
    echo "Start the app once to create it, or pass the correct path:" >&2
    echo "  ./seed-napboxes-linux.sh /full/path/to/pingdashboard.db" >&2
    exit 1
fi

echo "Using database: $DB_PATH"

# Make sure the table exists (in case the app hasn't created it yet).
sqlite3 "$DB_PATH" "CREATE TABLE IF NOT EXISTS Napboxes (Id INTEGER NOT NULL CONSTRAINT PK_Napboxes PRIMARY KEY AUTOINCREMENT, NapboxName TEXT NOT NULL);"

# Build one transaction with guarded inserts.
{
    echo "BEGIN TRANSACTION;"
    for name in "${NAPBOXES[@]}"; do
        # escape single quotes (none expected, but safe)
        safe="${name//\'/\'\'}"
        echo "INSERT INTO Napboxes (NapboxName) SELECT '$safe' WHERE NOT EXISTS (SELECT 1 FROM Napboxes WHERE NapboxName='$safe');"
    done
    echo "COMMIT;"
} | sqlite3 "$DB_PATH"

echo ""
echo "Current napboxes:"
sqlite3 -header -column "$DB_PATH" "SELECT Id, NapboxName FROM Napboxes ORDER BY NapboxName;"
echo ""
echo "Done."
