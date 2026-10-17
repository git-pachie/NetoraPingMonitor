#!/usr/bin/env bash
# install-service-linux.sh
# Installs PingDashboard as a systemd service so it:
#   - starts automatically on boot
#   - restarts automatically if it crashes or exits
#
# Usage:
#   chmod +x install-service-linux.sh
#   ./install-service-linux.sh
#
# Optional overrides (environment variables):
#   APP_DIR    directory containing the PingDashboard binary  (default: ~/PingDashboardApp)
#   RUN_USER   user to run the service as                     (default: current user)
#   SMTP_USER  Gmail address for notifications                (optional)
#   SMTP_PASS  Gmail APP password (16 chars, not account pw)  (optional)
#
# Example with SMTP:
#   SMTP_USER="sinetorawifi@gmail.com" SMTP_PASS="abcd efgh ijkl mnop" ./install-service-linux.sh

set -euo pipefail

APP_DIR="${APP_DIR:-$HOME/PingDashboardApp}"
RUN_USER="${RUN_USER:-$(id -un)}"
RUN_GROUP="$(id -gn "$RUN_USER")"
SERVICE_NAME="pingdashboard"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
BIN="$APP_DIR/PingDashboard"

# --- sanity checks -----------------------------------------------------------
if [[ ! -d "$APP_DIR" ]]; then
    echo "ERROR: app directory not found: $APP_DIR" >&2
    echo "Set APP_DIR to the folder containing the PingDashboard binary." >&2
    exit 1
fi

if [[ ! -f "$BIN" ]]; then
    echo "ERROR: PingDashboard binary not found at: $BIN" >&2
    exit 1
fi

chmod +x "$BIN" || true

# --- optional SMTP environment lines ----------------------------------------
SMTP_LINES=""
if [[ -n "${SMTP_USER:-}" ]]; then
    SMTP_LINES+=$'\n'"Environment=Smtp__Username=${SMTP_USER}"
fi
if [[ -n "${SMTP_PASS:-}" ]]; then
    SMTP_LINES+=$'\n'"Environment=Smtp__Password=${SMTP_PASS}"
fi

# --- write the unit file -----------------------------------------------------
echo "Creating systemd unit: $SERVICE_FILE"
sudo tee "$SERVICE_FILE" >/dev/null <<EOF
[Unit]
Description=PingDashboard network monitor web app
After=network.target

[Service]
Type=simple
User=${RUN_USER}
Group=${RUN_GROUP}
WorkingDirectory=${APP_DIR}
ExecStart=${BIN}

# Auto-restart on crash or exit
Restart=always
RestartSec=5

# Give the process a clean shutdown window
TimeoutStopSec=20
KillSignal=SIGINT

# Environment
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=ASPNETCORE_ENVIRONMENT=Production${SMTP_LINES}

# Hardening (loosen if the app needs more access)
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

# --- reload, enable, start ---------------------------------------------------
echo "Reloading systemd..."
sudo systemctl daemon-reload

echo "Enabling ${SERVICE_NAME} (start on boot)..."
sudo systemctl enable "${SERVICE_NAME}.service"

echo "Starting ${SERVICE_NAME}..."
sudo systemctl restart "${SERVICE_NAME}.service"

sleep 2
echo ""
echo "----------------------------------------------------------------------"
sudo systemctl status "${SERVICE_NAME}.service" --no-pager || true
echo "----------------------------------------------------------------------"
echo ""
echo "Done. Useful commands:"
echo "  sudo systemctl status  ${SERVICE_NAME}      # check status"
echo "  sudo systemctl restart ${SERVICE_NAME}      # restart now"
echo "  sudo systemctl stop    ${SERVICE_NAME}      # stop"
echo "  sudo systemctl disable ${SERVICE_NAME}      # don't start on boot"
echo "  journalctl -u ${SERVICE_NAME} -f            # live logs"
