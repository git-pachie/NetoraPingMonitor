# Netora Ping Monitor

A real-time network monitoring solution built with .NET 8. It continuously pings a configurable list of hosts and displays their connectivity status live in a web dashboard, updated instantly over SignalR — no page refresh needed. It also stores clients in SQLite, groups them by NAP box, logs connect/disconnect events, and can email alerts when a client's status changes.

The repository contains three .NET projects.

---

## Projects

### 1. PingDashboard — ASP.NET Core MVC + SignalR + SQLite

The web front end and the heart of the system.

- **Live status dashboard** — every client shown in a sortable, responsive table; status flips green (Connected) / red (Not Connected) the instant a ping result arrives over SignalR.
- **SQLite persistence** — clients, NAP boxes, and status logs are stored in `pingdashboard.db` (created automatically on first run).
- **Client CRUD** — add / edit / delete clients through a Bootstrap modal with validation and toast notifications.
- **NAP boxes** — a separate management page to create/edit/delete NAP box names; each client can be assigned to one via a dropdown.
- **Napbox column** on the dashboard is sortable and has a filter dropdown to show only clients in a selected NAP box.
- **Unknown clients** — any IP reported by the agent that isn't registered in the DB is grouped in its own section with a one-click "Add to DB" button.
- **Notifications** — each client can enable email alerts and store a comma-separated recipient list; a bell icon 🔔/🔕 shows the state per row.
- **Status logging** — connect/disconnect transitions are recorded to a `StatusLogs` table and shown on a live **Logs** page. Logged **only on a status change** so a continuously-reporting agent doesn't flood the log.
- **Email alerts** — on a status change, if the client has notifications enabled, an email is sent via a configurable SMTP server.
- **File logging** — client/napbox CRUD, status changes, and email outcomes are appended to a configurable log file.
- **Startup + periodic sweep** — 5 seconds after startup (and every 5 minutes after) the dashboard pings all clients itself and logs a full snapshot, so it works even before the agent connects.
- **Responsive + locale-aware** — mobile-friendly layout; timestamps render in the viewer's local timezone.

### 2. PingAgent — Console app

Reads a host list from `appsettings.json`, pings them on an interval, and pushes each result to the PingDashboard SignalR hub. Pings run in parallel, and it auto-connects/reconnects so it can start before or after the dashboard.

### 3. PingMonitor — Console app

A lightweight standalone logger. Pings a single host and writes a log entry whenever round-trip time exceeds a configurable threshold. Independent of the dashboard.

---

## Architecture

```
┌──────────────┐   ping    ┌───────────────┐              ┌────────────────────────┐
│   PingAgent  │ ────────► │  Target hosts │              │      PingDashboard     │
│  (console)   │           └───────────────┘              │   (MVC + SignalR Hub)  │
│              │ ──── ReportStatus (SignalR) ───────────► │           │            │
└──────────────┘                                          │           ▼            │
                                                          │   SQLite (clients,     │
     Dashboard also pings clients itself on a timer  ───► │   napboxes, logs)      │
                                                          │           │            │
                                                          │   ┌───────┼────────┐   │
                                                          │   ▼       ▼        ▼   │
                                                          │ Browser  File     SMTP │
                                                          │ (live)   log     email │
                                                          └────────────────────────┘
```

On each status report the hub checks the IP against the DB:
- **Known + status changed** → persist, log to file + DB, email if enabled, broadcast `ReceiveStatus` and `ReceiveLog`.
- **Known + unchanged** → just update the timestamp and broadcast the live status (no log, no email).
- **Unknown IP** → broadcast `ReceiveUnknown` (shown in the Unknown Clients section).

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

### Run the dashboard
```bash
dotnet run --project PingDashboard/PingDashboard.csproj --launch-profile http
```
Open http://localhost:5010. The SQLite database is created automatically on first launch.

### Run the ping agent
```bash
dotnet run --project PingAgent/PingAgent.csproj
```
Ensure `DashboardHubUrl` in `PingAgent/appsettings.json` matches the dashboard URL.

### Run the standalone monitor (optional)
```bash
dotnet run --project PingMonitor/PingMonitor.csproj
```

---

## Configuration

### Email / SMTP (PingDashboard)

Non-secret settings live in `PingDashboard/appsettings.json`:
```json
"Smtp": {
  "Enabled": true,
  "Host": "smtp.gmail.com",
  "Port": 587,
  "UseSsl": true,
  "FromAddress": "your-monitor@example.com",
  "FromName": "Ping Dashboard"
}
```

Credentials are kept out of source control using **.NET user secrets**:
```bash
cd PingDashboard
dotnet user-secrets set "Smtp:Username" "you@gmail.com"
dotnet user-secrets set "Smtp:Password" "your-app-password"
```
> For Gmail, use an **App Password** (with 2FA enabled), not your account password.
> Set `Smtp:Enabled` to `false` to disable sending (status changes are still logged).

### File logging (PingDashboard)

```json
"FileLog": {
  "Path": "logs/pingdashboard.log"
}
```
Relative paths resolve next to the executable. Logged categories: `APP`, `CLIENT`, `NAPBOX`, `STATUS`, `EMAIL`.

### PingAgent — `PingAgent/appsettings.json`
```json
"PingAgent": {
  "DashboardHubUrl": "http://localhost:5010/pinghub",
  "IntervalMs": 3000,
  "PingTimeoutMs": 5000,
  "Clients": [
    { "Name": "Google DNS Primary", "IpAddress": "8.8.8.8" }
  ]
}
```

### PingMonitor — `PingMonitor/appsettings.json`
```json
"PingMonitor": {
  "Host": "8.8.8.8",
  "ThresholdMs": 20,
  "IntervalMs": 1000,
  "LogFilePath": "ping_log.txt"
}
```

---

## Tech Stack

- **.NET 8** — all projects
- **ASP.NET Core MVC** — dashboard web app
- **SignalR** — real-time server↔browser and agent↔server messaging
- **Entity Framework Core + SQLite** — persistence
- **System.Net.Mail** — SMTP email
- **Bootstrap 5** — responsive UI

---

## Project Structure

```
NetoraPingMonitor/
├── PingDashboard/                 ASP.NET MVC + SignalR + SQLite dashboard
│   ├── Controllers/               Home, Clients, Napbox, Logs (+ JSON APIs)
│   ├── Data/                      AppDbContext, DbInitialiser
│   ├── Hubs/                      PingHub (SignalR)
│   ├── Models/                    Client, Napbox, StatusLog, ClientStatus
│   ├── Services/                  FileLogger, EmailSender, StartupPingCheck
│   ├── Views/                     Home, Napbox, Logs
│   └── wwwroot/                   dashboard.js, napbox.js, logs.js, css, signalr.min.js
├── PingAgent/                     Console pinger → pushes status to the hub
└── PingMonitor/                   Standalone threshold ping logger
```

---

## Notes

- SMTP sending has not been verified against a live mail server in this environment; configure real credentials and watch the `[EMAIL]` lines in the log file to confirm delivery.
- The SQLite database and log files are excluded from git via `.gitignore`.
