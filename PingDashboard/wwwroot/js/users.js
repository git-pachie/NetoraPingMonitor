/**
 * users.js — User management (Admin only)
 * CRUD against /api/usersapi: profile (email/mobile/image), role, napbox
 * assignments, and password. Image uploads go to /api/usersapi/{id}/image.
 */
(function () {
    "use strict";

    var modalEl = document.getElementById("userModal");
    var bsModal = new bootstrap.Modal(modalEl);

    // Cache of users by id (populated from the API) so Edit can prefill fields.
    var usersById = {};

    function showToast(msg, type) {
        var box = document.getElementById("toastBox");
        var text = document.getElementById("toastMsg");
        box.className = "toast align-items-center border-0 text-white";
        box.classList.add(type === "danger" ? "bg-danger" : type === "success" ? "bg-success" : "bg-info");
        text.textContent = msg;
        bootstrap.Toast.getOrCreateInstance(box, { delay: 3500 }).show();
    }

    function escHtml(s) {
        return String(s == null ? "" : s)
            .replace(/&/g,"&amp;").replace(/"/g,"&quot;").replace(/</g,"&lt;").replace(/>/g,"&gt;");
    }

    function updateCount() {
        var el = document.getElementById("userCount");
        if (el) el.textContent = document.querySelectorAll("#userBody tr[id]").length;
    }

    function renumber() {
        document.querySelectorAll("#userBody tr[id]").forEach(function (row, idx) {
            var n = row.querySelector(".row-num");
            if (n) n.textContent = idx + 1;
        });
    }

    function roleBadge(role) {
        return role === "Admin"
            ? '<span class="badge bg-primary">Admin</span>'
            : '<span class="badge bg-secondary">Technician</span>';
    }

    function avatarHtml(user) {
        if (user.profileImagePath)
            return '<img src="' + escHtml(user.profileImagePath) + '" class="avatar" alt="" />';
        var letter = (user.username || "?").charAt(0).toUpperCase();
        return '<span class="avatar avatar-placeholder">' + escHtml(letter) + '</span>';
    }

    // ── Load full user data for edit prefill ────────────────────────────────────
    function refreshUserCache() {
        return fetch("/api/usersapi")
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (list) {
                usersById = {};
                list.forEach(function (u) { usersById[u.id] = u; });
            })
            .catch(function () { /* ignore */ });
    }

    // ── Modal ──────────────────────────────────────────────────────────────────
    function clearErrors() {
        ["inputUsername", "inputPassword"].forEach(function (id) {
            document.getElementById(id).classList.remove("is-invalid");
        });
        document.getElementById("usernameError").textContent = "";
        document.getElementById("passwordError").textContent = "";
    }

    function setPreview(path, username) {
        var img = document.getElementById("imgPreview");
        var ph  = document.getElementById("imgPlaceholder");
        if (path) {
            img.src = path; img.style.display = "";
            ph.style.display = "none";
        } else {
            img.style.display = "none";
            ph.style.display = "";
            ph.textContent = (username || "?").charAt(0).toUpperCase();
        }
    }

    function setNapboxChecks(selected) {
        var set = {};
        (selected || []).forEach(function (n) { set[n.toLowerCase()] = true; });
        document.querySelectorAll(".napbox-check").forEach(function (cb) {
            cb.checked = !!set[cb.value.toLowerCase()];
        });
    }

    function getCheckedNapboxes() {
        return Array.prototype.map.call(
            document.querySelectorAll(".napbox-check:checked"),
            function (cb) { return cb.value; });
    }

    // Napbox assignment only matters for Technicians; disable when Admin.
    function toggleNapboxGroup() {
        var isAdmin = document.getElementById("inputRole").value === "Admin";
        var grp = document.getElementById("napboxGroup");
        grp.style.opacity = isAdmin ? "0.5" : "1";
        document.querySelectorAll(".napbox-check").forEach(function (cb) { cb.disabled = isAdmin; });
    }

    function openAdd() {
        document.getElementById("userModalLabel").textContent = "Add User";
        document.getElementById("editId").value = "0";
        document.getElementById("inputUsername").value = "";
        document.getElementById("inputUsername").disabled = false;
        document.getElementById("inputEmail").value = "";
        document.getElementById("inputMobile").value = "";
        document.getElementById("inputPassword").value = "";
        document.getElementById("inputRole").value = "Technician";
        document.getElementById("inputImage").value = "";
        document.getElementById("pwHint").textContent = "(required)";
        document.getElementById("imgHint").textContent = "Uploaded after the user is created (max 2 MB).";
        setPreview(null, "");
        setNapboxChecks([]);
        toggleNapboxGroup();
        clearErrors();
        bsModal.show();
    }

    function openEdit(id) {
        var u = usersById[id];
        if (!u) { showToast("User not found — refresh the page.", "danger"); return; }
        document.getElementById("userModalLabel").textContent = "Edit User";
        document.getElementById("editId").value = id;
        document.getElementById("inputUsername").value = u.username;
        document.getElementById("inputUsername").disabled = true;
        document.getElementById("inputEmail").value = u.email || "";
        document.getElementById("inputMobile").value = u.mobile || "";
        document.getElementById("inputPassword").value = "";
        document.getElementById("inputRole").value = u.role;
        document.getElementById("inputImage").value = "";
        document.getElementById("pwHint").textContent = "(leave blank to keep current)";
        document.getElementById("imgHint").textContent = "Uploads immediately on selection (max 2 MB).";
        setPreview(u.profileImagePath, u.username);
        setNapboxChecks(u.napboxes || []);
        toggleNapboxGroup();
        clearErrors();
        bsModal.show();
    }

    // ── Row upsert ─────────────────────────────────────────────────────────────
    function upsertRow(user) {
        usersById[user.id] = user;
        var placeholder = document.getElementById("noDataRow");
        if (placeholder) placeholder.remove();

        var rowId = "usr-" + user.id;
        var tbody = document.getElementById("userBody");
        var row = document.getElementById(rowId);

        if (!row) {
            row = document.createElement("tr");
            row.id = rowId;
            row.setAttribute("data-id", user.id);
            var count = tbody.querySelectorAll("tr[id]").length;
            row.innerHTML =
                '<td class="row-num">' + (count + 1) + '</td>' +
                '<td class="usr-avatar"></td>' +
                '<td class="usr-name"></td>' +
                '<td class="d-none d-md-table-cell usr-email text-muted small"></td>' +
                '<td class="d-none d-lg-table-cell usr-mobile text-muted small"></td>' +
                '<td class="usr-role"></td>' +
                '<td class="d-none d-lg-table-cell usr-napboxes text-muted small"></td>' +
                '<td class="text-center">' +
                  '<button class="btn btn-xs btn-outline-primary me-1 btn-edit"></button>' +
                  '<button class="btn btn-xs btn-outline-danger btn-delete"></button>' +
                '</td>';
            tbody.appendChild(row);
            updateCount();
        }

        row.querySelector(".usr-avatar").innerHTML = avatarHtml(user);
        row.querySelector(".usr-name").textContent = user.username;
        row.querySelector(".usr-email").textContent = user.email || "—";
        row.querySelector(".usr-mobile").textContent = user.mobile || "—";
        row.querySelector(".usr-role").innerHTML = roleBadge(user.role);
        row.querySelector(".usr-napboxes").textContent =
            user.role === "Admin" ? "All" : ((user.napboxes && user.napboxes.length) ? user.napboxes.join(", ") : "—");

        var editBtn = row.querySelector(".btn-edit");
        editBtn.textContent = "Edit";
        editBtn.dataset.id = user.id;

        var delBtn = row.querySelector(".btn-delete");
        delBtn.textContent = "Del";
        delBtn.dataset.id = user.id;
        delBtn.dataset.name = user.username;
    }

    // ── Image upload ────────────────────────────────────────────────────────────
    function uploadImage(id) {
        var fileInput = document.getElementById("inputImage");
        if (!fileInput.files || fileInput.files.length === 0) return Promise.resolve(null);
        var fd = new FormData();
        fd.append("file", fileInput.files[0]);
        return fetch("/api/usersapi/" + id + "/image", { method: "POST", body: fd })
            .then(function (r) {
                if (r.ok) return r.json().then(function (res) { return res.profileImagePath; });
                return r.json().then(function (err) { throw new Error((err && err.message) || "Image upload failed."); });
            });
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────
    function save() {
        clearErrors();
        var id       = parseInt(document.getElementById("editId").value, 10);
        var username = document.getElementById("inputUsername").value.trim();
        var email    = document.getElementById("inputEmail").value.trim();
        var mobile   = document.getElementById("inputMobile").value.trim();
        var password = document.getElementById("inputPassword").value;
        var role     = document.getElementById("inputRole").value;
        var napboxes = getCheckedNapboxes();
        var isNew    = id === 0;

        var valid = true;
        if (isNew && !username) {
            document.getElementById("inputUsername").classList.add("is-invalid");
            document.getElementById("usernameError").textContent = "Username is required.";
            valid = false;
        }
        if (isNew && !password) {
            document.getElementById("inputPassword").classList.add("is-invalid");
            document.getElementById("passwordError").textContent = "Password is required.";
            valid = false;
        }
        if (!valid) return;

        var url    = isNew ? "/api/usersapi" : "/api/usersapi/" + id;
        var method = isNew ? "POST" : "PUT";
        var body   = { username: username, email: email, mobile: mobile,
                       password: password, role: role, napboxes: napboxes };

        fetch(url, {
            method: method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        })
        .then(function (r) {
            if (r.ok) return r.json().then(function (user) {
                // Upload image (if any) now that we have the user id
                return uploadImage(user.id)
                    .then(function (path) {
                        if (path) user.profileImagePath = path;
                        bsModal.hide();
                        showToast(isNew ? "User created." : "User updated.", "success");
                        upsertRow(user);
                    })
                    .catch(function (e) {
                        // User saved but image failed — still show the row
                        bsModal.hide();
                        upsertRow(user);
                        showToast(e.message, "danger");
                    });
            });
            return r.json().then(function (err) {
                var msg = (err && err.message) ? err.message : "Save failed.";
                if (r.status === 409) {
                    document.getElementById("inputUsername").classList.add("is-invalid");
                    document.getElementById("usernameError").textContent = msg;
                } else {
                    showToast(msg, "danger");
                }
            });
        })
        .catch(function () { showToast("Network error.", "danger"); });
    }

    function del(id, name) {
        if (!confirm('Delete user "' + name + '"?')) return;
        fetch("/api/usersapi/" + id, { method: "DELETE" })
        .then(function (r) {
            if (r.ok) {
                var row = document.getElementById("usr-" + id);
                if (row) { row.remove(); renumber(); updateCount(); }
                delete usersById[id];
                showToast('"' + name + '" deleted.', "success");
                if (!document.querySelector("#userBody tr[id]"))
                    document.getElementById("userBody").innerHTML =
                        '<tr id="noDataRow"><td colspan="8" class="text-center text-muted py-4">No users.</td></tr>';
            } else {
                r.json().then(function (err) {
                    showToast((err && err.message) ? err.message : "Delete failed.", "danger");
                }).catch(function () { showToast("Delete failed.", "danger"); });
            }
        })
        .catch(function () { showToast("Network error.", "danger"); });
    }

    // Live preview when a file is chosen
    document.getElementById("inputImage").addEventListener("change", function () {
        if (this.files && this.files[0]) {
            var reader = new FileReader();
            reader.onload = function (e) { setPreview(e.target.result, ""); };
            reader.readAsDataURL(this.files[0]);
        }
    });

    document.getElementById("inputRole").addEventListener("change", toggleNapboxGroup);

    document.getElementById("userBody").addEventListener("click", function (e) {
        var btn = e.target.closest("button");
        if (!btn) return;
        if (btn.classList.contains("btn-edit")) openEdit(btn.dataset.id);
        else if (btn.classList.contains("btn-delete")) del(btn.dataset.id, btn.dataset.name);
    });

    document.getElementById("btnAddUser").addEventListener("click", openAdd);
    document.getElementById("btnSave").addEventListener("click", save);

    // Prime the cache so Edit has full data (email/mobile/napboxes/image).
    refreshUserCache();

})();
