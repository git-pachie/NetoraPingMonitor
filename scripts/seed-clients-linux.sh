#!/usr/bin/env bash
# seed-clients-linux.sh
# Self-contained: inserts monitored clients into the PingDashboard SQLite DB on Linux.
# Idempotent — keyed on IpAddress, so re-running will not create duplicates.
# New clients start with status Unknown (IsConnected = NULL); the PingAgent/dashboard
# updates status at runtime. Notifications are enabled since an e-mail is provided.
#
# Usage:
#   chmod +x seed-clients-linux.sh
#   ./seed-clients-linux.sh                                   # default DB path
#   ./seed-clients-linux.sh ~/PingDashboardApp/pingdashboard.db
#
# Requires sqlite3:  sudo apt-get update && sudo apt-get install -y sqlite3
# Stop the app first so the DB isn't locked:  pkill -9 -f PingDashboard

set -euo pipefail

DB_PATH="${1:-$HOME/PingDashboardApp/pingdashboard.db}"

# Rows: NapboxName | Name | NotifyEmail | IpAddress   (pipe-separated)
CLIENTS=(
    "ALJOHN|Kaingin Aljohn|archieangeles@gmail.com|172.23.104.202"
    "ALJOHN|Kaingin Melody|archieangeles@gmail.com|172.23.104.203"
    "ALJOHN|Kaingin Jave|archieangeles@gmail.com|172.23.104.205"
    "ALJOHN|Kaingin Jacky Bondad|archieangeles@gmail.com|172.23.104.206"
    "ALJOHN|Kaingin RC Guinto|archieangeles@gmail.com|172.23.104.209"
    "ANDREI|Norte Andrei|archieangeles@gmail.com|172.23.102.80"
    "ARABELA|Khay Cee|archieangeles@gmail.com|172.23.102.44"
    "ARABELA|Glen Aguilar|archieangeles@gmail.com|172.23.102.52"
    "ARABELA|Lorna Pangilinan|archieangeles@gmail.com|172.23.102.53"
    "ARABELA|Len Castilio|archieangeles@gmail.com|172.23.102.57"
    "ARABELA|Tita Elsie|archieangeles@gmail.com|172.23.102.61"
    "ARABELA|Megan Pangilinan|archieangeles@gmail.com|172.23.102.62"
    "BIMBO|Glency|archieangeles@gmail.com|172.23.104.210"
    "BIMBO|Epoy|archieangeles@gmail.com|172.23.104.211"
    "BRGYHALL1|Sumacab Brgy Hall|archieangeles@gmail.com|172.23.103.42"
    "BRGYHALL1|Amparo Valdez|archieangeles@gmail.com|172.23.103.53"
    "BRGYHALL1|MJ|archieangeles@gmail.com|172.23.103.55"
    "ROBERT|Bantayog Apartment Danilo|archieangeles@gmail.com|172.23.103.56"
    "BRGYHALL1|Julius Serrano|archieangeles@gmail.com|172.23.103.54"
    "BRGYHALL1|Jan Gamboa|archieangeles@gmail.com|172.23.103.57"
    "BRGYHALL1|Edna Ramirez|archieangeles@gmail.com|172.23.103.59"
    "BRGYHALL1|Sherley Pamiloza|archieangeles@gmail.com|172.23.103.61"
    "CHICELLE|Aileen|archieangeles@gmail.com|172.23.102.24"
    "CHICELLE|Chicelle|archieangeles@gmail.com|172.23.102.25"
    "CHICELLE|Mark Bryan|archieangeles@gmail.com|172.23.102.32"
    "COMPOUND|Mercy|archieangeles@gmail.com|172.23.101.11"
    "COMPOUND|Tita Chie|archieangeles@gmail.com|172.23.102.12"
    "COMPOUND|Norby|archieangeles@gmail.com|172.23.101.12"
    "COMPOUND|Jocelyn Bote|archieangeles@gmail.com|172.23.101.13"
    "COMPOUND|Tita Vicky|archieangeles@gmail.com|172.23.101.14"
    "COMPOUND|Lola Tessie|archieangeles@gmail.com|172.23.101.15"
    "COMPOUND|Hazel|archieangeles@gmail.com|172.23.101.16"
    "COMPOUND|Tito Pitong|archieangeles@gmail.com|172.23.102.13"
    "FLORANTE|House Ivy|archieangeles@gmail.com|172.23.102.26"
    "FLORANTE|Florante|archieangeles@gmail.com|172.23.102.41"
    "FLORANTE|Evelyn Parungao|archieangeles@gmail.com|172.23.102.43"
    "IGLESIA|Rovie|archieangeles@gmail.com|172.23.102.22"
    "IGLESIA|Lorraine Gara|archieangeles@gmail.com|172.23.102.23"
    "IGLESIA|Donalyn Santos|archieangeles@gmail.com|172.23.102.45"
    "IGLESIA|Janet Cabahil|archieangeles@gmail.com|172.23.102.59"
    "ITLUGAN|Janlee Jacinto|archieangeles@gmail.com|172.23.104.101"
    "ITLUGAN|Acquel Sayo|archieangeles@gmail.com|172.23.104.217"
    "ITLUGAN|Nilo Feliciano|archieangeles@gmail.com|172.23.103.60"
    "JACKYBAYAN|Rochelle Pangilinan|archieangeles@gmail.com|172.23.102.30"
    "JACKYBAYAN|Store Ivy|archieangeles@gmail.com|172.23.102.27"
    "JACKYBAYAN|Jaylyn Mateo|archieangeles@gmail.com|172.23.102.47"
    "JACKYBAYAN|Jackie Bayan|archieangeles@gmail.com|172.23.102.49"
    "JHAY|Jay Ramos|archieangeles@gmail.com|172.23.103.40"
    "OGIE|Ogie|archieangeles@gmail.com|172.23.102.35"
    "OGIE|Gia Baguisa|archieangeles@gmail.com|172.23.102.40"
    "OGIE|Ritchelle Vergara|archieangeles@gmail.com|172.23.102.50"
    "OGIE|Ben Magtalas|archieangeles@gmail.com|172.23.102.51"
    "EXTENSION|Ritchelle Vergara - Extension|archieangeles@gmail.com|172.23.102.55"
    "OGIE|Jazzmin Tumibay|archieangeles@gmail.com|172.23.102.42"
    "ROBERT|Robert|archieangeles@gmail.com|172.23.103.38"
    "ROBERT|Danilo Tumibay House|archieangeles@gmail.com|172.23.102.33"
    "SAPA1|Clamosa Tumibay|archieangeles@gmail.com|172.23.102.15"
    "SAPA1|Janine1|archieangeles@gmail.com|172.23.102.17"
    "SAPA1|Janine2|archieangeles@gmail.com|172.23.102.18"
    "SAPA1|Paco|archieangeles@gmail.com|172.23.102.19"
    "SAPA1|Ryan Soriano|archieangeles@gmail.com|172.23.102.16"
    "SAPA1|Allen Bactol|archieangeles@gmail.com|172.23.102.20"
    "SAPA1|Eliseo|archieangeles@gmail.com|172.23.102.21"
    "SAPA2|Maika Tumibay|archieangeles@gmail.com|172.23.104.231"
    "SAPA2|Delly Anne|archieangeles@gmail.com|172.23.104.232"
    "SWEETIE|Rosalie Mariano|archieangeles@gmail.com|172.23.104.218"
)

if ! command -v sqlite3 >/dev/null 2>&1; then
    echo "ERROR: sqlite3 is not installed. Run:" >&2
    echo "  sudo apt-get update && sudo apt-get install -y sqlite3" >&2
    exit 1
fi

if [[ ! -f "$DB_PATH" ]]; then
    echo "ERROR: database not found: $DB_PATH" >&2
    echo "Start the app once to create it, or pass the correct path:" >&2
    echo "  ./seed-clients-linux.sh /full/path/to/pingdashboard.db" >&2
    exit 1
fi

# escape single quotes for SQL string literals
sql_escape() { printf '%s' "${1//\'/\'\'}"; }

echo "Using database: $DB_PATH"

{
    echo "BEGIN TRANSACTION;"
    for row in "${CLIENTS[@]}"; do
        IFS='|' read -r napbox name email ip <<< "$row"
        napbox=$(sql_escape "$napbox")
        name=$(sql_escape "$name")
        email=$(sql_escape "$email")
        ip=$(sql_escape "$ip")
        # Insert only if no client with this IP exists yet.
        echo "INSERT INTO Clients (Name, IpAddress, IsConnected, LastReceivedTime, NotifyEmail, IsNotificationEnabled, NapboxName)
              SELECT '$name', '$ip', NULL, NULL, '$email', 1, '$napbox'
              WHERE NOT EXISTS (SELECT 1 FROM Clients WHERE IpAddress='$ip');"
    done
    echo "COMMIT;"
} | sqlite3 "$DB_PATH"

echo ""
echo "Current clients:"
sqlite3 -header -column "$DB_PATH" \
    "SELECT Id, NapboxName AS Napbox, Name, IpAddress AS IP FROM Clients ORDER BY NapboxName, Name;"
echo ""
echo "Total: $(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM Clients;") clients in database."
echo "Done."
