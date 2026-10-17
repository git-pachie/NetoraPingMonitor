#!/usr/bin/env bash
# insert-napboxes.sh
# Runs insert-napboxes.sql against the PingDashboard SQLite database.
#
# Usage:
#   ./insert-napboxes.sh [path-to-pingdashboard.db]
#
# If no path is given, it defaults to pingdashboard.db next to this script's
# parent app folder. Adjust DB_DEFAULT if your database lives elsewhere.
#
# Requirements: sqlite3 CLI  (install with: sudo apt-get install -y sqlite3)
#
# IMPORTANT: Stop the PingDashboard app before running so the DB isn't locked:
#   pkill -9 -f PingDashboard

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_FILE="$SCRIPT_DIR/insert-napboxes.sql"

# Default DB location: adjust to wherever pingdashboard.db actually is.
DB_DEFAULT="$HOME/PingDashboardApp/pingdashboard.db"
DB_PATH="${1:-$DB_DEFAULT}"

if ! command -v sqlite3 >/dev/null 2>&1; then
    echo "ERROR: sqlite3 is not installed. Install it with:" >&2
    echo "  sudo apt-get update && sudo apt-get install -y sqlite3" >&2
    exit 1
fi

if [[ ! -f "$SQL_FILE" ]]; then
    echo "ERROR: SQL file not found: $SQL_FILE" >&2
    exit 1
fi

if [[ ! -f "$DB_PATH" ]]; then
    echo "ERROR: database not found: $DB_PATH" >&2
    echo "Pass the correct path:  ./insert-napboxes.sh /path/to/pingdashboard.db" >&2
    exit 1
fi

echo "Using database: $DB_PATH"
echo "Applying napbox inserts (idempotent)..."
sqlite3 "$DB_PATH" < "$SQL_FILE"
echo "Done."
