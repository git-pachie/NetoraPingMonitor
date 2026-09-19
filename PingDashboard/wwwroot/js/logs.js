/**
 * logs.js — Status Logs page
 * Shows connect/disconnect events; receives new entries live via SignalR,
 * and can clear all logs.
 */
(function () {
    "use strict";

    // ── Time formatting to local region ──────────────────────────────────────
    function formatTime(raw) {
        if (!raw) return "—";
        try {
            var iso = raw.replace(" UTC", "").replace(" ", "T") + "Z";
            var d = new Date(iso);
            if (isNaN(d.getTime())) return raw;
            return d.toLocaleString(undefined, {
                year: "numeric", month: "2-digit", day: "2-digit",
                hour: "2-digit", minute: "2-digit", second: "2-digit"
            });
        } catch (e) { return raw; }
    }

    // Convert any server-rendered UTC timestamps to local on load
    function localiseExisting() {
        document.querySelectorAll(".log-time[data-utc]").forEach(function (el) {
            el.textContent = formatTime(el.getAttribute("data-utc"));
        });
    }

    // ── Toast ────────────────────────────────────────────────────────────────
    function showToast(msg, type) {
        var box = document.getElementById("toastBox");
        var text = document.getElementById("toastMsg");
        box.className = "toast align-items-center border-0 text-white";
        box.classList.add(type === "danger" ? "bg-danger" :
                          type === "success" ? "bg-success" : "bg-info");
        text.textContent = msg;
        bootstrap.Toast.getOrCreateInstance(box, { delay: 3000 }).show();
    }

    function eventBadge(isConnected) {
        return isConnected
            ? '<span class="badge bg-success px-2 py-1">Connected</span>'
            : '<span class="badge bg-danger px-2 py-1">Disconnected</span>';
    }

    function escHtml(s) {
        return String(s).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;");
    }

    function renumber() {
        document.querySelectorAll("#logBody tr").forEach(function (row, idx) {
            var c = row.querySelector(".idx");
            if (c) c.textContent = idx + 1;
        });
    }

    // Prepend a new log entry at the top
    function prependLog(data) {
        var placeholder = document.getElementById("noDataRow");
        if (placeholder) placeholder.remove();

        var tbody = document.getElementById("logBody");
        var row = document.createElement("tr");
        row.innerHTML =
            '<td class="idx"></td>' +
            '<td>' + escHtml(data.clientName) + '</td>' +
            '<td class="d-none d-md-table-cell"><code>' + escHtml(data.ipAddress) + '</code></td>' +
            '<td>' + eventBadge(data.isConnected) + '</td>' +
            '<td class="text-muted small">' + formatTime(data.timestamp) + '</td>';
        tbody.insertBefore(row, tbody.firstChild);

        // Flash the new row
        row.classList.add("row-flash");

        renumber();
    }

    // Give existing server-rendered rows an .idx cell for renumbering
    function tagIndexCells() {
        document.querySelectorAll("#logBody tr").forEach(function (row) {
            var first = row.querySelector("td");
            if (first && !first.classList.contains("idx")) first.classList.add("idx");
        });
    }

    // ── Clear logs ─────────────────────────────────────────────────────────────
    document.getElementById("btnClearLogs").addEventListener("click", function () {
        if (!confirm("Clear all log entries? This cannot be undone.")) return;
        fetch("/api/statuslogs", { method: "DELETE" })
            .then(function (r) {
                if (r.ok) {
                    document.getElementById("logBody").innerHTML =
                        '<tr id="noDataRow"><td colspan="5" class="text-center text-muted py-4">' +
                        'No log entries yet.</td></tr>';
                    showToast("All logs cleared.", "success");
                } else {
                    showToast("Failed to clear logs.", "danger");
                }
            })
            .catch(function () { showToast("Network error.", "danger"); });
    });

    // ── Init ─────────────────────────────────────────────────────────────────
    localiseExisting();
    tagIndexCells();

    // ── SignalR (live log updates) ─────────────────────────────────────────────
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/pinghub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    function setLive(on) {
        var badge = document.getElementById("liveBadge");
        if (!badge) return;
        badge.className = "badge px-2 py-2 " + (on ? "bg-success" : "bg-secondary");
        badge.innerHTML = '<span class="dot ' + (on ? "dot-success" : "dot-secondary") + '"></span> Live';
    }

    connection.on("ReceiveLog", function (data) { prependLog(data); });
    connection.onreconnecting(function () { setLive(false); });
    connection.onreconnected(function () { setLive(true); });
    connection.onclose(function () { setLive(false); });

    connection.start()
        .then(function () { setLive(true); })
        .catch(function (err) { setLive(false); console.error("SignalR failed:", err.toString()); });

})();
