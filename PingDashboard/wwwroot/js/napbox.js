/**
 * napbox.js — Napbox Management
 * CRUD against /api/napboxes with a Bootstrap modal + toast notifications.
 */
(function () {
    "use strict";

    var modalEl = document.getElementById("napboxModal");
    var bsModal = new bootstrap.Modal(modalEl);

    // ── Toast ────────────────────────────────────────────────────────────────
    function showToast(msg, type) {
        var box  = document.getElementById("toastBox");
        var text = document.getElementById("toastMsg");
        box.className = "toast align-items-center border-0 text-white";
        switch (type) {
            case "success": box.classList.add("bg-success"); break;
            case "danger":  box.classList.add("bg-danger");  break;
            default:        box.classList.add("bg-info");
        }
        text.textContent = msg;
        bootstrap.Toast.getOrCreateInstance(box, { delay: 3500 }).show();
    }

    function escHtml(s) {
        return String(s)
            .replace(/&/g,"&amp;").replace(/"/g,"&quot;")
            .replace(/</g,"&lt;").replace(/>/g,"&gt;");
    }

    function updateCount() {
        var el = document.getElementById("napboxCount");
        if (el) el.textContent = document.querySelectorAll("#napboxBody tr[id]").length;
    }

    function renumber() {
        document.querySelectorAll("#napboxBody tr[id]").forEach(function (row, idx) {
            var n = row.querySelector(".row-num");
            if (n) n.textContent = idx + 1;
        });
    }

    // ── Modal ────────────────────────────────────────────────────────────────
    function clearErrors() {
        document.getElementById("inputNapboxName").classList.remove("is-invalid");
        document.getElementById("nameError").textContent = "";
    }

    function openAdd() {
        document.getElementById("napboxModalLabel").textContent = "Add Napbox";
        document.getElementById("editId").value = "0";
        document.getElementById("inputNapboxName").value = "";
        clearErrors();
        bsModal.show();
    }

    function openEdit(id, name) {
        document.getElementById("napboxModalLabel").textContent = "Edit Napbox";
        document.getElementById("editId").value = id;
        document.getElementById("inputNapboxName").value = name;
        clearErrors();
        bsModal.show();
    }

    // ── Row upsert ─────────────────────────────────────────────────────────────
    function upsertRow(box) {
        var placeholder = document.getElementById("noDataRow");
        if (placeholder) placeholder.remove();

        var rowId = "nap-" + box.id;
        var tbody = document.getElementById("napboxBody");
        var row   = document.getElementById(rowId);

        if (!row) {
            row = document.createElement("tr");
            row.id = rowId;
            row.setAttribute("data-id", box.id);
            var count = tbody.querySelectorAll("tr[id]").length;
            row.innerHTML =
                '<td class="row-num">' + (count + 1) + '</td>' +
                '<td class="nap-name"></td>' +
                '<td class="text-center">' +
                  '<button class="btn btn-xs btn-outline-primary me-1 btn-edit"></button>' +
                  '<button class="btn btn-xs btn-outline-danger btn-delete"></button>' +
                '</td>';
            tbody.appendChild(row);
            updateCount();
        }

        row.querySelector(".nap-name").textContent = box.napboxName;

        var editBtn = row.querySelector(".btn-edit");
        editBtn.textContent = "Edit";
        editBtn.dataset.id = box.id;
        editBtn.dataset.name = box.napboxName;

        var delBtn = row.querySelector(".btn-delete");
        delBtn.textContent = "Del";
        delBtn.dataset.id = box.id;
        delBtn.dataset.name = box.napboxName;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────
    function save() {
        clearErrors();
        var id   = parseInt(document.getElementById("editId").value, 10);
        var name = document.getElementById("inputNapboxName").value.trim();

        if (!name) {
            document.getElementById("inputNapboxName").classList.add("is-invalid");
            document.getElementById("nameError").textContent = "Napbox name is required.";
            return;
        }

        var url    = id === 0 ? "/api/napboxes" : "/api/napboxes/" + id;
        var method = id === 0 ? "POST" : "PUT";

        fetch(url, {
            method:  method,
            headers: { "Content-Type": "application/json" },
            body:    JSON.stringify({ napboxName: name })
        })
        .then(function (r) {
            if (r.ok) return r.json().then(function (box) {
                bsModal.hide();
                showToast(id === 0 ? "Napbox added." : "Napbox updated.", "success");
                upsertRow(box);
            });
            return r.json().then(function (err) {
                var msg = (err && err.message) ? err.message : "Save failed.";
                if (r.status === 409) {
                    document.getElementById("inputNapboxName").classList.add("is-invalid");
                    document.getElementById("nameError").textContent = msg;
                } else {
                    showToast(msg, "danger");
                }
            });
        })
        .catch(function () { showToast("Network error. Please try again.", "danger"); });
    }

    function del(id, name) {
        if (!confirm('Delete "' + name + '"?')) return;
        fetch("/api/napboxes/" + id, { method: "DELETE" })
        .then(function (r) {
            if (r.ok) {
                var row = document.getElementById("nap-" + id);
                if (row) { row.remove(); renumber(); updateCount(); }
                showToast('"' + name + '" deleted.', "success");
                if (!document.querySelector("#napboxBody tr[id]"))
                    document.getElementById("napboxBody").innerHTML =
                        '<tr id="noDataRow"><td colspan="3" class="text-center text-muted py-4">' +
                        'No napboxes yet — click <strong>Add Napbox</strong> to create one.</td></tr>';
            } else {
                showToast("Delete failed.", "danger");
            }
        })
        .catch(function () { showToast("Network error.", "danger"); });
    }

    // ── Wire events ────────────────────────────────────────────────────────────
    document.getElementById("napboxBody").addEventListener("click", function (e) {
        var btn = e.target.closest("button");
        if (!btn) return;
        if (btn.classList.contains("btn-edit"))
            openEdit(btn.dataset.id, btn.dataset.name);
        else if (btn.classList.contains("btn-delete"))
            del(btn.dataset.id, btn.dataset.name);
    });

    document.getElementById("btnAddNapbox").addEventListener("click", openAdd);
    document.getElementById("btnSave").addEventListener("click", save);

    // Enter key in the input triggers save
    document.getElementById("inputNapboxName").addEventListener("keydown", function (e) {
        if (e.key === "Enter") { e.preventDefault(); save(); }
    });

})();
