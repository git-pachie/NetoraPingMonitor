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

    // ── Filter state ───────────────────────────────────────────────────────────
    var napboxFilter = "";   // "" = show all

    // ── Napbox visibility scope (Technician) ────────────────────────────────────
    // window.allowedNapboxes is null for Admin (see everything) or an array of
    // napbox names the Technician is assigned to.
    var allowedSet = null;
    if (Array.isArray(window.allowedNapboxes)) {
        allowedSet = {};
        window.allowedNapboxes.forEach(function (n) {
            if (n) allowedSet[String(n).toLowerCase()] = true;
        });
    }
    function napboxAllowed(name) {
        if (allowedSet === null) return true;            // Admin
        if (!name) return false;                          // techs never see unassigned clients
        return !!allowedSet[String(name).toLowerCase()];
    }

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

    function actionButtons(id, name, ip, email, notify, napbox) {
        // Technicians have no edit/delete controls
        if (!window.isAdmin) return "";
        var n = escHtml(name), p = escHtml(ip), e = escHtml(email || ""),
            nt = notify ? "1" : "0", nb = escHtml(napbox || "");
        return '<button class="btn btn-xs btn-outline-primary me-1 btn-edit"' +
               ' data-id="' + id + '" data-name="' + n + '" data-ip="' + p + '"' +
               ' data-email="' + e + '" data-notify="' + nt + '" data-napbox="' + nb + '" title="Edit">✏</button>' +
               '<button class="btn btn-xs btn-outline-danger btn-delete"' +
               ' data-id="' + id + '" data-name="' + n + '" title="Delete">✕</button>';
    }

    function bellIcon(enabled, email) {
        if (enabled)
            return '<span class="bell-on" title="Notifications ON: ' + escHtml(email || "") + '">🔔</span>';
        return '<span class="bell-off" title="Notifications OFF">🔕</span>';
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
        if (col === "napbox") {
            var nb = (row.getAttribute("data-napbox") || "").trim().toLowerCase();
            // Empty napbox sorts to the bottom
            return nb === "" ? "~" : nb;
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
        applyNapboxFilter();
    }

    // Show/hide rows based on the selected napbox filter, then renumber visible rows.
    function applyNapboxFilter() {
        var rows = document.querySelectorAll("#statusBody tr[id]");
        var visible = 0;
        rows.forEach(function(row) {
            var nb = (row.getAttribute("data-napbox") || "").trim();
            var show = (napboxFilter === "") || (nb === napboxFilter);
            row.style.display = show ? "" : "none";
            if (show) {
                visible++;
                var n = row.querySelector(".row-num");
                if (n) n.textContent = visible;
            }
        });
        // Update the client count to reflect the filtered view
        var countEl = document.getElementById("clientCount");
        if (countEl) {
            countEl.textContent = napboxFilter === ""
                ? rows.length
                : visible + " of " + rows.length;
        }
        // Show a "no match" note row if the filter hides everything
        var tbody = document.getElementById("statusBody");
        var noMatch = document.getElementById("noMatchRow");
        if (napboxFilter !== "" && visible === 0 && rows.length > 0) {
            if (!noMatch) {
                noMatch = document.createElement("tr");
                noMatch.id = "noMatchRow";
                noMatch.innerHTML =
                    '<td colspan="8" class="text-center text-muted py-4">' +
                    'No clients in this napbox.</td>';
                tbody.appendChild(noMatch);
            }
        } else if (noMatch) {
            noMatch.remove();
        }
    }

    function updateSortIcons() {
        ["name","status","napbox"].forEach(function(col) {
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
                '<td class="client-napbox text-muted small"></td>' +
                '<td class="text-center client-notify"></td>' +
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
        row.setAttribute("data-napbox", data.napboxName || "");
        var napboxEl = row.querySelector(".client-napbox");
        if (napboxEl) napboxEl.textContent = data.napboxName || "—";
        var notifyEl = row.querySelector(".client-notify");
        if (notifyEl) notifyEl.innerHTML = bellIcon(data.isNotificationEnabled, data.notifyEmail);
        var timeEl = row.querySelector(".client-time");
        if (timeEl) timeEl.textContent = timeStr;
        var actEl = row.querySelector(".client-actions");
        if (actEl) actEl.innerHTML = actionButtons(data.id, data.name, data.ipAddress, data.notifyEmail, data.isNotificationEnabled, data.napboxName);

        applySortAndRenumber();
        applyNapboxFilter();   // ensure new/updated rows respect the active filter
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
        ["inputName","inputIp","inputNotifyEmail"].forEach(function(id) {
            var el = document.getElementById(id);
            if (el) el.classList.remove("is-invalid");
        });
        document.getElementById("nameError").textContent  = "";
        document.getElementById("ipError").textContent    = "";
        var em = document.getElementById("emailError");
        if (em) em.textContent = "";
    }

    // Set the napbox <select> value; if the stored value isn't an option
    // (e.g. the napbox was deleted), add it as a temporary option so it shows.
    function setNapboxSelect(value) {
        var sel = document.getElementById("inputNapbox");
        if (!sel) return;
        value = value || "";
        var found = Array.prototype.some.call(sel.options, function (o) { return o.value === value; });
        if (value && !found) {
            var opt = document.createElement("option");
            opt.value = value;
            opt.textContent = value + " (not in list)";
            sel.appendChild(opt);
        }
        sel.value = value;
    }

    function openAddModal(prefillName, prefillIp) {
        document.getElementById("clientModalLabel").textContent = "Add Client";
        document.getElementById("editId").value              = "0";
        document.getElementById("inputName").value           = prefillName || "";
        document.getElementById("inputIp").value             = prefillIp   || "";
        document.getElementById("inputNotifyEmail").value    = "";
        document.getElementById("inputNotifyEnabled").checked = false;
        setNapboxSelect("");
        clearModalErrors();
        bsModal.show();
    }

    function openEditModal(id, name, ip, email, notify, napbox) {
        document.getElementById("clientModalLabel").textContent = "Edit Client";
        document.getElementById("editId").value              = id;
        document.getElementById("inputName").value           = name;
        document.getElementById("inputIp").value             = ip;
        document.getElementById("inputNotifyEmail").value    = email || "";
        document.getElementById("inputNotifyEnabled").checked = (notify === "1" || notify === true);
        setNapboxSelect(napbox);
        clearModalErrors();
        bsModal.show();
    }

    window.openAddModal  = openAddModal;
    window.openEditModal = openEditModal;

    // Load napbox options from the API into the modal dropdown
    function loadNapboxes() {
        fetch("/api/napboxes")
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (boxes) {
                // 1) Modal dropdown
                var sel = document.getElementById("inputNapbox");
                if (sel) {
                    sel.innerHTML = '<option value="">— None —</option>';
                    boxes.forEach(function (b) {
                        var opt = document.createElement("option");
                        opt.value = b.napboxName;
                        opt.textContent = b.napboxName;
                        sel.appendChild(opt);
                    });
                }
                // 2) Header filter dropdown (preserve current selection)
                var filter = document.getElementById("filterNapbox");
                if (filter) {
                    var current = filter.value;
                    filter.innerHTML = '<option value="">All napboxes</option>';
                    boxes.forEach(function (b) {
                        var opt = document.createElement("option");
                        opt.value = b.napboxName;
                        opt.textContent = b.napboxName;
                        filter.appendChild(opt);
                    });
                    // restore selection if it still exists
                    filter.value = current;
                    if (filter.value !== current) { napboxFilter = ""; filter.value = ""; }
                }
            })
            .catch(function () { /* dropdowns just stay with defaults */ });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CRUD
    // ─────────────────────────────────────────────────────────────────────────
    function saveClient() {
        clearModalErrors();
        var id       = parseInt(document.getElementById("editId").value, 10);
        var name     = document.getElementById("inputName").value.trim();
        var ip       = document.getElementById("inputIp").value.trim();
        var email    = document.getElementById("inputNotifyEmail").value.trim();
        var notifyOn = document.getElementById("inputNotifyEnabled").checked;
        var napbox   = document.getElementById("inputNapbox").value;
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
        // Validate each comma-separated email if any provided
        if (email) {
            var parts = email.split(",").map(function(s){ return s.trim(); }).filter(Boolean);
            var re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
            var bad = parts.filter(function(p){ return !re.test(p); });
            if (bad.length) {
                document.getElementById("inputNotifyEmail").classList.add("is-invalid");
                document.getElementById("emailError").textContent = "Invalid email: " + bad.join(", ");
                valid = false;
            } else {
                email = parts.join(", "); // normalise spacing
            }
        }
        // If notifications enabled, require at least one email
        if (notifyOn && !email) {
            document.getElementById("inputNotifyEmail").classList.add("is-invalid");
            document.getElementById("emailError").textContent = "Add at least one email to enable notifications.";
            valid = false;
        }
        if (!valid) return;

        var url    = id === 0 ? "/api/clients" : "/api/clients/" + id;
        var method = id === 0 ? "POST" : "PUT";

        fetch(url, {
            method:  method,
            headers: { "Content-Type": "application/json" },
            body:    JSON.stringify({
                name: name,
                ipAddress: ip,
                notifyEmail: email,
                isNotificationEnabled: notifyOn,
                napboxName: napbox
            })
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
                        : null,
                    notifyEmail:           client.notifyEmail,
                    isNotificationEnabled: client.isNotificationEnabled,
                    napboxName:            client.napboxName
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
                        '<tr id="noDataRow"><td colspan="8" class="text-center text-muted py-4">' +
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
    loadNapboxes();

    document.getElementById("statusBody").addEventListener("click", function(e) {
        var btn = e.target.closest("button");
        if (!btn) return;
        if (btn.classList.contains("btn-edit"))
            openEditModal(btn.dataset.id, btn.dataset.name, btn.dataset.ip, btn.dataset.email, btn.dataset.notify, btn.dataset.napbox);
        else if (btn.classList.contains("btn-delete"))
            deleteClient(btn.dataset.id, btn.dataset.name);
    });

    document.getElementById("unknownBody").addEventListener("click", function(e) {
        var btn = e.target.closest(".btn-add-unknown");
        if (btn) openAddModal(btn.dataset.name, btn.dataset.ip);
    });

    var addBtn = document.getElementById("btnAddClient");
    if (addBtn) addBtn.addEventListener("click", function() { openAddModal(); });
    var saveBtn = document.getElementById("btnSave");
    if (saveBtn) saveBtn.addEventListener("click", saveClient);

    ["name","status","napbox"].forEach(function(col) {
        var th = document.getElementById("th-" + col);
        if (th) th.addEventListener("click", function() { onSortClick(col); });
    });

    // Napbox filter dropdown
    var filterEl = document.getElementById("filterNapbox");
    if (filterEl) {
        filterEl.addEventListener("change", function() {
            napboxFilter = this.value;
            applyNapboxFilter();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SIGNALR
    // ─────────────────────────────────────────────────────────────────────────
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/pinghub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on("ReceiveStatus",  function(data) {
        // Technicians only see clients in their assigned napboxes
        if (!napboxAllowed(data.napboxName)) return;
        upsertKnownRow(data);
    });
    connection.on("ReceiveUnknown", function(data) {
        // Unknown (unassigned) clients are hidden from Technicians entirely
        if (allowedSet !== null) return;
        upsertUnknownRow(data);
    });

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
