# Netora Ping Monitor

A real-time network monitoring solution built with .NET 8. It continuously pings a configurable list of hosts and displays their connectivity status live in a web dashboard, updated instantly over SignalR — no page refresh needed.

The repository contains three independent .NET projects that work together (or standalone).

---

## Projects

### 1. PingDashboard (ASP.NET Core MVC + SignalR + SQLite)

The web front end. Displays every monitored client in a live table and lets you manage them.

- **Live status** pushed over SignalR — status flips green (Connected) / red (Not Connected) the moment a ping result arrives.
- **SQLite database** stores the client list (Name, IP Address, Status, Last Received Time). The schema is created automatically on first run.
- **Full CRUD** — add, edit, and delete clients through a Bootstrap modal with inline validation.
- **Toast notifications** confirm saves, deletes, and errors.
- **Unknown Clients section** — any IP reported by the agent that is *not* in the database is grouped separately, with a one-click "Add to DB" button to promote it.
- **Sortable columns** — click Name or Status to sort ascending/descending.
- **Responsive layout** — mobile-friendly; secondary columns collapse into the name cell on small screens.
- **Locale-aware timestamps** — "Last Received Time" is rendered in the viewer's local timezone and regional format.

### 2. PingAgent (Console app)

The monitoring worker. Reads a list of hosts from `appsettings.json`, pings them on an interval, and pushes each result to the PingDashboard SignalR hub.

- Pings all configured hosts in parallel each cycle.
- Automatically connects (and reconnects) to the hub with backoff — it can be started before or after the dashboard.
- Supports IP addresses and hostnames.

### 3. PingMonitor (Console app)

A lightweight standalone logger. Continuously pings a single host (default `8.8.8.8`) and writes a log entry whenever the round-trip time exceeds a configurable threshold. Self-contained — does not require the dashboard.

---

## Architecture

```
┌──────────────┐   ping    ┌───────────────┐   SignalR    ┌────────────────────┐
│   PingAgent  │ ────────► │  Target hosts │              │    PingDashboard   │
│  (console)   │           └───────────────┘              │  (MVC + SignalR)   │
│              │ ───────────  ReportStatus  ────────────► │        Hub         │
└──────────────┘                                          │         │          │
                                                          │         ▼          │
                                                          │   SQLite (clients) │
                                                          │         │          │
                                                          │         ▼          │
                                                          │  Browser dashboard │
                                                          │  (live updates)    │
                                                          └────────────────────┘
```

1. **PingAgent** pings each host and calls the hub method `ReportStatus(name, ip, isConnected)`.
2. The **PingHub** looks up the IP in SQLite:
   - Known IP → persists status and broadcasts `ReceiveStatus` to browsers.
   - Unknown IP → broadcasts `ReceiveUnknown` (shown in the Unknown Clients section).
3. The **browser** receives the event and updates the corresponding table row in real time.

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

### Run the dashboard

```bash
dotnet run --project PingDashboard/PingDashboard.csproj --launch-profile http
```

Then open http://localhost:5010. The SQLite database (`pingdashboard.db`) is created automatically on first launch.

### Run the ping agent

In a second terminal:

```bash
dotnet run --project PingAgent/PingAgent.csproj
```

Make sure `DashboardHubUrl` in `PingAgent/appsettings.json` matches the dashboard's URL.

### Run the standalone monitor (optional)

```bash
dotnet run --project PingMonitor/PingMonitor.csproj
```

---

## Configuration

### PingAgent — `PingAgent/appsettings.json`

```json
{
  "PingAgent": {
    "DashboardHubUrl": "http://localhost:5010/pinghub",
    "IntervalMs": 3000,
    "PingTimeoutMs": 5000,
    "Clients": [
      { "Name": "Google DNS Primary", "IpAddress": "8.8.8.8" },
      { "Name": "Cloudflare DNS",     "IpAddress": "1.1.1.1" }
    ]
  }
}
```

| Setting | Description |
|---|---|
| `DashboardHubUrl` | URL of the PingDashboard SignalR hub |
| `IntervalMs` | Delay between ping sweeps (ms) |
| `PingTimeoutMs` | Per-ping timeout (ms) |
| `Clients` | List of hosts to monitor (Name + IP/hostname) |

### PingMonitor — `PingMonitor/appsettings.json`

```json
{
  "PingMonitor": {
    "Host": "8.8.8.8",
    "ThresholdMs": 20,
    "IntervalMs": 1000,
    "LogFilePath": "ping_log.txt"
  }
}
```

| Setting | Description |
|---|---|
| `Host` | Host to ping |
| `ThresholdMs` | Log an entry when RTT exceeds this value |
| `IntervalMs` | Delay between pings (ms) |
| `LogFilePath` | Log file path (relative to the executable, or absolute) |

---

## Tech Stack

- **.NET 8** — all three projects
- **ASP.NET Core MVC** — dashboard web app
- **SignalR** — real-time server↔browser and agent↔server messaging
- **Entity Framework Core + SQLite** — client persistence
- **Bootstrap 5** — responsive UI

---

## Project Structure

```
NetoraPingMonitor/
├── PingDashboard/          ASP.NET MVC + SignalR + SQLite dashboard
│   ├── Controllers/        HomeController, ClientsController (CRUD API)
│   ├── Data/               AppDbContext, DbInitialiser
│   ├── Hubs/               PingHub (SignalR)
│   ├── Models/             Client, ClientStatus
│   ├── Services/           ClientStatusStore
│   ├── Views/              Index.cshtml, _Layout.cshtml
│   └── wwwroot/            dashboard.js, dashboard.css, signalr.min.js
├── PingAgent/              Console pinger → pushes status to the hub
└── PingMonitor/            Standalone threshold ping logger
```
