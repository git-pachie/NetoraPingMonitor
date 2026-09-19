/**
 * dashboard.js — Network Monitor
 * SignalR live updates · CRUD · modal · toast · sort · responsive mobile rows
 */
(function () {
    "use strict";

    var modalEl = document.getElementById("clientModal");
    var bsModal = new bootstrap.Modal(modalEl);

    // ── Sort state ─────────────────────────────────────────────────────────────
    var sortCol = null;
    var sortDir = "asc";

    // ─────────────────────────────────────────────────────────────────────────
    // NORMALISE RAZOR-RENDERED ROWS
    // Razor emits plain text directly inside <td class="client-name">.
    // JS-created rows use <span class="name-text"> inside that td.
    // This function retrofits all existing rows so upsertKnownRow works on both.
    // ─────────────────────────────────────────────────────────────────────────
    function normaliseExistingRows() {
        var rows = document.querySelectorAll("#statusBody tr[id]");
        rows.forEach(function (row) {
            var nameTd = row.querySelector(".client-name");
            if (!nameTd) return;

            // Already normalised (has .name-text span)?
            if (nameTd.querySelector(".name-text")) return;

            // Grab the raw text node (first child node that is a text node)
            var rawText = "";
            nameTd.childNodes.forEach(function (node) {
                if (node.nodeType === Node.TEXT_NODE)
                    rawText += node.textContent;
            });
            rawText = rawText.trim();

            // Rebuild the cell keeping the existing mobile divs
            var ipMobile   = nameTd.querySelector(".client-ip-mobile");
            var timeMobile = nameTd.querySelector(".client-time-mobile");

            nameTd.innerHTML = "";

            var span = document.createElement("span");
            span.className   = "name-text";
            span.textContent = rawText;
            nameTd.appendChild(span);

            if (ipMobile)   nameTd.appendChild(ipMobile);
            if (timeMobile) nameTd.appendChild(timeMobile);

            // Ensure actions cell has delegated-event-compatible buttons
            // (Razor already renders these correctly, nothing to do)
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TIME — format UTC string from server into the browser's local region
    // The server sends "yyyy-MM-dd HH:mm:ss UTC"
    // We parse it, then display using the browser locale + timezone.
    // ─────────────────────────────────────────────────────────────────────────
    function formatTime(raw) {
        if (!raw || raw === "—") return "—";
        try {
            // Strip " UTC" suffix if present, replace space with T, append Z
            var iso = raw.replace(" UTC", "").replace(" ", "T") + "Z";
            var d   = new Date(iso);
            if (isNaN(d.getTime())) return raw;
            return d.toLocaleString(undefined, {
                year:   "numeric",
                month:  "2-digit",
                day:    "2-digit",
                hour:   "2-digit",
                minute: "2-digit",
                second: "2-digit"
            });
        } catch (e) {
            return raw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TOAST
    // ─────────────────────────────────────────────────────────────────────────
    function showToast(msg, type) {
        var box  = document.getElementById("toastBox");
        var text = document.getElementById("toastMsg");
        box.className = "toast align-items-center border-0 text-white";
        switch (type) {
            case "success": box.classList.add("bg-success"); break;
            case "danger":  box.classList.add("bg-danger");  break;
            case "warning": box.classList.add("bg-warning", "text-dark"); break;
            default:        box.classList.add("bg-info");
        }
        text.textContent = msg;
        bootstrap.Toast.getOrCreateInstance(box, { delay: 3500 }).show();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────
    function statusBadge(isConnected) {
        if (isConnected === true)
            return '<span class="badge bg-success px-2 py-1"><span class="dot dot-success"></span> Connected</span>';
        if (isConnected === false)
            return '<span class="badge bg-danger px-2 py-1"><span class="dot dot-danger"></span> Not Connected</span>';
        return '<span class="badge bg-secondary px-2 py-1">Unknown</span>';
    }

    function actionButtons(id, name, ip) {
        var n = escHtml(name), p = escHtml(ip);
        return '<button class="btn btn-xs btn-outline-primary me-1 btn-edit"' +
               ' data-id="' + id + '" data-name="' + n + '" data-ip="' + p + '" title="Edit">✏</button>' +
               '<button class="btn btn-xs btn-outline-danger btn-delete"' +
               ' data-id="' + id + '" data-name="' + n + '" title="Delete">✕</button>';
    }

    function escHtml(s) {
        return String(s)
            .replace(/&/g,"&amp;").replace(/"/g,"&quot;")
            .replace(/</g,"&lt;").replace(/>/g,"&gt;");
    }

    function touchRefresh() {
        var el = document.getElementById("lastRefresh");
        if (el) el.textContent = new Date().toLocaleTimeString();
    }

    function updateClientCount() {
        var el = document.getElementById("clientCount");
        if (el) el.textContent = document.querySelectorAll("#statusBody tr[id]").length;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SORT
    // ─────────────────────────────────────────────────────────────────────────
    function getSortKey(row, col) {
        if (col === "name") {
            var td = row.querySelector(".client-name");
            return td ? td.childNodes[0].textContent.trim().toLowerCase() : "";
        }
        if (col === "status") {
            var st = row.querySelector(".client-status .badge");
            if (!st) return "z";
            return st.classList.contains("bg-success") ? "a" :
                   st.classList.contains("bg-danger")  ? "b" : "c";
        }
        return "";
    }

    function applySortAndRenumber() {
        if (!sortCol) return;
        var tbody = document.getElementById("statusBody");
        if (!tbody) return;
        var rows = Array.prototype.slice.call(tbody.querySelectorAll("tr[id]"));
        if (!rows.length) return;
        rows.sort(function(a, b) {
            var ka = getSortKey(a, sortCol), kb = getSortKey(b, sortCol);
            var cmp = ka < kb ? -1 : ka > kb ? 1 : 0;
            return sortDir === "asc" ? cmp : -cmp;
        });
        rows.forEach(function(row, idx) {
            tbody.appendChild(row);
            var n = row.querySelector(".row-num");
            if (n) n.textContent = idx + 1;
        });
    }

    function updateSortIcons() {
        ["name","status"].forEach(function(col) {
            var icon = document.getElementById("sort-icon-" + col);
            var th   = document.getElementById("th-" + col);
            if (!icon || !th) return;
            if (col === sortCol) {
                icon.textContent = sortDir === "asc" ? " ▲" : " ▼";
                th.classList.add("sort-active");
            } else {
                icon.textContent = " ⇅";
                th.classList.remove("sort-active");
            }
        });
    }

    function onSortClick(col) {
        sortDir = (sortCol === col && sortDir === "asc") ? "desc" : "asc";
        sortCol = col;
        updateSortIcons();
        applySortAndRenumber();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KNOWN CLIENTS — row upsert
    // Columns hidden on small screens get a mobile sub-line inside the Name cell.
    // ─────────────────────────────────────────────────────────────────────────
    function upsertKnownRow(data) {
        var placeholder = document.getElementById("noDataRow");
        if (placeholder) placeholder.remove();

        var timeStr = formatTime(data.lastReceivedTime);
        var rowId   = "row-" + data.id;
        var tbody   = document.getElementById("statusBody");
        var row     = document.getElementById(rowId);

        if (!row) {
            row = document.createElement("tr");
            row.id = rowId;
            row.setAttribute("data-id", data.id);
            row.setAttribute("data-ip", data.ipAddress);
            var count = tbody.querySelectorAll("tr[id]").length;
            row.innerHTML =
                '<td class="row-num d-none d-sm-table-cell">' + (count + 1) + '</td>' +
                '<td class="client-name">' +
                  '<span class="name-text"></span>' +
                  '<div class="d-md-none text-muted small client-ip-mobile"></div>' +
                  '<div class="d-lg-none text-muted small client-time-mobile"></div>' +
                '</td>' +
                '<td class="d-none d-md-table-cell"><code class="client-ip"></code></td>' +
                '<td class="client-status"></td>' +
                '<td class="d-none d-lg-table-cell text-muted small client-time"></td>' +
                '<td class="text-center client-actions"></td>';
            tbody.appendChild(row);
            updateClientCount();
        }

        var nameEl = row.querySelector(".name-text");
        if (nameEl) nameEl.textContent = data.name;
        var ipMobEl = row.querySelector(".client-ip-mobile");
        if (ipMobEl) ipMobEl.textContent = data.ipAddress;
        var timeMobEl = row.querySelector(".client-time-mobile");
        if (timeMobEl) timeMobEl.textContent = timeStr;
        var codeEl = row.querySelector(".client-ip");
        if (codeEl) codeEl.textContent = data.ipAddress;
        row.querySelector(".client-status").innerHTML = statusBadge(data.isConnected);
        var timeEl = row.querySelector(".client-time");
        if (timeEl) timeEl.textContent = timeStr;
        var actEl = row.querySelector(".client-actions");
        if (actEl) actEl.innerHTML = actionButtons(data.id, data.name, data.ipAddress);

        applySortAndRenumber();
        row.classList.remove("row-flash"); void row.offsetWidth; row.classList.add("row-flash");
        touchRefresh();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UNKNOWN CLIENTS
    // ─────────────────────────────────────────────────────────────────────────
    function upsertUnknownRow(data) {
        document.getElementById("unknownSection").style.display = "";
        var timeStr = formatTime(data.lastReceivedTime);
        var safeId  = "unk-" + data.ipAddress.replace(/[^a-zA-Z0-9]/g, "-");
        var tbody   = document.getElementById("unknownBody");
        var row     = document.getElementById(safeId);

        if (!row) {
            row = document.createElement("tr");
            row.id = safeId;
            var count = tbody.querySelectorAll("tr").length;
            row.innerHTML =
                '<td class="d-none d-sm-table-cell">' + (count + 1) + '</td>' +
                '<td class="client-name">' +
                  '<span class="name-text"></span>' +
                  '<div class="d-md-none text-muted small client-ip-mobile"></div>' +
                  '<div class="d-lg-none text-muted small client-time-mobile"></div>' +
                '</td>' +
                '<td class="d-none d-md-table-cell"><code class="client-ip"></code></td>' +
                '<td class="client-status"></td>' +
                '<td class="d-none d-lg-table-cell text-muted small client-time"></td>' +
                '<td class="text-center">' +
                  '<button class="btn btn-xs btn-warning btn-add-unknown"' +
                  ' data-name="' + escHtml(data.name) + '" data-ip="' + escHtml(data.ipAddress) + '">Add</button>' +
                '</td>';
            tbody.appendChild(row);
        }

        row.querySelector(".name-text").textContent          = data.name;
        row.querySelector(".client-ip-mobile").textContent   = data.ipAddress;
        row.querySelector(".client-time-mobile").textContent = timeStr;
        var codeEl = row.querySelector(".client-ip");
        if (codeEl) codeEl.textContent = data.ipAddress;
        row.querySelector(".client-status").innerHTML = statusBadge(data.isConnected);
        var timeEl = row.querySelector(".client-time");
        if (timeEl) timeEl.textContent = timeStr;

        row.classList.remove("row-flash"); void row.offsetWidth; row.classList.add("row-flash");
        touchRefresh();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTION BADGE
    // ─────────────────────────────────────────────────────────────────────────
    function setConnectionState(state) {
        var badge = document.getElementById("connectionBadge");
        var dot   = document.getElementById("connectionDot");
        var text  = document.getElementById("connectionText");
        if (!badge || !dot || !text) return;
        badge.className = "badge px-2 py-2 d-flex align-items-center gap-1";
        dot.className   = "dot";
        switch (state) {
            case "connected":
                badge.classList.add("bg-success"); dot.classList.add("dot-success");
                text.textContent = "Connected"; break;
            case "reconnecting":
                badge.classList.add("bg-warning","text-dark"); dot.classList.add("dot-warning");
                text.textContent = "Reconnecting…"; break;
            case "disconnected":
                badge.classList.add("bg-danger"); dot.classList.add("dot-danger");
                text.textContent = "Disconnected"; break;
            default:
                badge.classList.add("bg-secondary"); dot.classList.add("dot-secondary");
                text.textContent = "Connecting…";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MODAL
    // ─────────────────────────────────────────────────────────────────────────
    function clearModalErrors() {
        ["inputName","inputIp"].forEach(function(id) {
            document.getElementById(id).classList.remove("is-invalid");
        });
        document.getElementById("nameError").textContent = "";
        document.getElementById("ipError").textContent   = "";
    }

    function openAddModal(prefillName, prefillIp) {
        document.getElementById("clientModalLabel").textContent = "Add Client";
        document.getElementById("editId").value    = "0";
        document.getElementById("inputName").value = prefillName || "";
        document.getElementById("inputIp").value   = prefillIp   || "";
        clearModalErrors();
        bsModal.show();
    }

    function openEditModal(id, name, ip) {
        document.getElementById("clientModalLabel").textContent = "Edit Client";
        document.getElementById("editId").value    = id;
        document.getElementById("inputName").value = name;
        document.getElementById("inputIp").value   = ip;
        clearModalErrors();
        bsModal.show();
    }

    window.openAddModal  = openAddModal;
    window.openEditModal = openEditModal;

    // ─────────────────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────────────────
    function saveClient() {
        clearModalErrors();
        var id   = parseInt(document.getElementById("editId").value, 10);
        var name = document.getElementById("inputName").value.trim();
        var ip   = document.getElementById("inputIp").value.trim();
        var valid = true;
        if (!name) {
            document.getElementById("inputName").classList.add("is-invalid");
            document.getElementById("nameError").textContent = "Name is required.";
            valid = false;
        }
        if (!ip) {
            document.getElementById("inputIp").classList.add("is-invalid");
            document.getElementById("ipError").textContent = "IP / Hostname is required.";
            valid = false;
        }
        if (!valid) return;

        var url    = id === 0 ? "/api/clients" : "/api/clients/" + id;
        var method = id === 0 ? "POST" : "PUT";

        fetch(url, {
            method:  method,
            headers: { "Content-Type": "application/json" },
            body:    JSON.stringify({ name: name, ipAddress: ip })
        })
        .then(function(r) {
            if (r.ok) return r.json().then(function(client) {
                bsModal.hide();
                showToast(id === 0 ? "Client added." : "Client updated.", "success");
                upsertKnownRow({
                    id:              client.id,
                    name:            client.name,
                    ipAddress:       client.ipAddress,
                    isConnected:     client.isConnected,
                    lastReceivedTime: client.lastReceivedTime
                        ? client.lastReceivedTime.replace("T"," ").substring(0,19) + " UTC"
                        : null
                });
                removeFromUnknown(client.ipAddress);
            });
            return r.json().then(function(err) {
                var msg = (err && err.message) ? err.message : "Save failed.";
                if (r.status === 409) {
                    document.getElementById("inputIp").classList.add("is-invalid");
                    document.getElementById("ipError").textContent = msg;
                } else {
                    showToast(msg, "danger");
                }
            });
        })
        .catch(function() { showToast("Network error. Please try again.", "danger"); });
    }

    function deleteClient(id, name) {
        if (!confirm('Delete "' + name + '"?')) return;
        fetch("/api/clients/" + id, { method: "DELETE" })
        .then(function(r) {
            if (r.ok) {
                var row = document.getElementById("row-" + id);
                if (row) { row.remove(); renumberKnown(); updateClientCount(); }
                showToast('"' + name + '" deleted.', "success");
                if (!document.querySelector("#statusBody tr[id]"))
                    document.getElementById("statusBody").innerHTML =
                        '<tr id="noDataRow"><td colspan="6" class="text-center text-muted py-4">' +
                        'No clients — click <strong>Add</strong> to get started.</td></tr>';
            } else {
                showToast("Delete failed.", "danger");
            }
        })
        .catch(function() { showToast("Network error.", "danger"); });
    }

    window.deleteClient = deleteClient;

    function removeFromUnknown(ip) {
        var row = document.getElementById("unk-" + ip.replace(/[^a-zA-Z0-9]/g, "-"));
        if (row) row.remove();
        if (!document.querySelector("#unknownBody tr"))
            document.getElementById("unknownSection").style.display = "none";
    }

    function renumberKnown() {
        document.querySelectorAll("#statusBody tr[id]").forEach(function(row, idx) {
            var n = row.querySelector(".row-num");
            if (n) n.textContent = idx + 1;
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INITIALISE — normalise server-rendered rows then wire events
    // ─────────────────────────────────────────────────────────────────────────
    normaliseExistingRows();

    document.getElementById("statusBody").addEventListener("click", function(e) {
        var btn = e.target.closest("button");
        if (!btn) return;
        if (btn.classList.contains("btn-edit"))
            openEditModal(btn.dataset.id, btn.dataset.name, btn.dataset.ip);
        else if (btn.classList.contains("btn-delete"))
            deleteClient(btn.dataset.id, btn.dataset.name);
    });

    document.getElementById("unknownBody").addEventListener("click", function(e) {
        var btn = e.target.closest(".btn-add-unknown");
        if (btn) openAddModal(btn.dataset.name, btn.dataset.ip);
    });

    document.getElementById("btnAddClient").addEventListener("click", function() { openAddModal(); });
    document.getElementById("btnSave").addEventListener("click", saveClient);

    ["name","status"].forEach(function(col) {
        var th = document.getElementById("th-" + col);
        if (th) th.addEventListener("click", function() { onSortClick(col); });
    });

    // ─────────────────────────────────────────────────────────────────────────
    // SIGNALR
    // ─────────────────────────────────────────────────────────────────────────
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/pinghub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on("ReceiveStatus",  function(data) { upsertKnownRow(data);   });
    connection.on("ReceiveUnknown", function(data) { upsertUnknownRow(data); });

    connection.onreconnecting(function() { setConnectionState("reconnecting"); });
    connection.onreconnected(function()  { setConnectionState("connected");    });
    connection.onclose(function()        { setConnectionState("disconnected"); });

    connection.start()
        .then(function() { setConnectionState("connected"); })
        .catch(function(err) {
            setConnectionState("disconnected");
            console.error("SignalR failed:", err.toString());
        });

})();
