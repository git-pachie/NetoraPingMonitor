#!/usr/bin/env bash
# test-email-linux.sh
# Sends ONE test email through Gmail SMTP using the same settings the app uses,
# so you can confirm credentials/network work without waiting for a status change.
#
# Usage:
#   SMTP_USER="you@gmail.com" SMTP_PASS="16charAppPassword" TO="dest@example.com" ./test-email-linux.sh
#
# Checks outbound connectivity first, then attempts an authenticated send.

set -euo pipefail

HOST="${SMTP_HOST:-smtp.gmail.com}"
PORT="${SMTP_PORT:-587}"
FROM="${SMTP_USER:?Set SMTP_USER to your Gmail address}"
PASS="${SMTP_PASS:?Set SMTP_PASS to your Gmail APP password}"
TO="${TO:-$FROM}"

echo "1) Testing outbound connectivity to $HOST:$PORT ..."
if command -v nc >/dev/null 2>&1; then
    if nc -vz -w 8 "$HOST" "$PORT" 2>&1 | grep -qiE "succeeded|open|connected"; then
        echo "   OK: port $PORT is reachable."
    else
        echo "   WARNING: could not confirm connection to $HOST:$PORT."
        echo "   Your host may block outbound SMTP. Email will fail if so."
    fi
else
    echo "   (nc not installed; skipping connectivity check. Install: sudo apt-get install -y netcat-openbsd)"
fi

echo "2) Sending test email via curl SMTP ..."
if ! command -v curl >/dev/null 2>&1; then
    echo "ERROR: curl is required. Install: sudo apt-get install -y curl" >&2
    exit 1
fi

TMP_MAIL="$(mktemp)"
trap 'rm -f "$TMP_MAIL"' EXIT
cat > "$TMP_MAIL" <<EOF
From: Ping Dashboard <$FROM>
To: <$TO>
Subject: PingDashboard SMTP test
Date: $(date -R)

This is a test email from the PingDashboard SMTP test script.
If you received this, outbound email from the Linux server works.
EOF

# STARTTLS on 587 (same as the app: Port 587 + UseSsl)
if curl --ssl-reqd --url "smtp://$HOST:$PORT" \
        --mail-from "$FROM" --mail-rcpt "$TO" \
        --user "$FROM:$PASS" \
        --upload-file "$TMP_MAIL" --silent --show-error; then
    echo ""
    echo "SUCCESS: email accepted by $HOST. Check the inbox for $TO."
else
    echo ""
    echo "FAILED: see the curl error above."
    echo "Common causes:"
    echo "  - 'authentication failed' -> use a Gmail APP PASSWORD (2FA must be on), not your account password."
    echo "  - 'Could not resolve host' / timeout -> DNS or firewall blocks outbound SMTP (587)."
    exit 1
fi
