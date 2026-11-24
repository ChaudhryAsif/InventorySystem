// WhatsApp page: connection status and QR, which group gets which document, and the recent sends.
(function () {
    const app = document.getElementById('waApp'); if (!app) return;
    const byId = id => document.getElementById(id);
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const base = '/WhatsAppGroup/';
    let groups = null, destinations = [], polling = false;

    async function call(action, method, body) {
        try {
            const response = await fetch(base + action, {
                method: method || 'GET',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'X-CSRF-TOKEN': csrf() },
                body: body ? JSON.stringify(body) : (method === 'POST' ? '{}' : undefined)
            });
            const text = await response.text(); let data = null;
            try { data = text ? JSON.parse(text) : null; } catch { }
            if (response.status === 401) return { ok: false, message: 'Your session expired. Sign in again.' };
            if (response.status === 403) return { ok: false, message: 'You do not have permission to manage WhatsApp.' };
            return { ok: response.ok && data?.success !== false, data, message: data?.message };
        } catch { return { ok: false, message: 'The server could not be reached.' }; }
    }
    function notice(message) { const n = byId('waNotice'); n.textContent = message || ''; n.hidden = !message; }

    // ---- connection ----
    function show(id, visible) { byId(id).hidden = !visible; }
    function renderStatus(s) {
        const dot = byId('waDot'), text = byId('waStatusText'), error = byId('waError');
        let label, kind, canConnect = false, canDisconnect = false;
        if (!s.configured) { label = 'Not set up on this server'; kind = 'bad'; }
        else if (!s.reachable) { label = 'The WhatsApp service cannot be reached'; kind = 'bad'; }
        else if (s.status === 'connected') { label = 'Connected as +' + (s.number || ''); kind = 'ok'; canDisconnect = true; }
        else if (s.status === 'qr') { label = 'Waiting for the code to be scanned'; kind = 'wait'; canDisconnect = true; }
        else if (s.status === 'disconnected') { label = 'Disconnected'; kind = 'bad'; canConnect = true; }
        else { label = 'Starting…'; kind = 'wait'; canDisconnect = true; }
        dot.className = 'wa-dot ' + kind; byId('waPill').className = 'wa-pill ' + kind; text.textContent = label;
        error.textContent = s.error || ''; error.hidden = !s.error;
        show('waQrBox', s.status === 'qr' && !!s.qr && s.reachable);
        if (s.qr) byId('waQr').src = s.qr;
        show('waConnect', canConnect); show('waDisconnect', canDisconnect);
        if (s.status === 'connected' && s.reachable && groups === null) loadGroups();
    }
    async function refreshStatus() {
        if (polling) return; polling = true;
        try {
            const r = await call('Status');
            if (r.data) renderStatus(r.data); else notice(r.message);
        } finally { polling = false; }
    }
    // The button is kept in a variable: event.currentTarget is empty once the handler has awaited.
    byId('waConnect').onclick = async e => {
        const button = e.currentTarget; button.disabled = true; notice('');
        const r = await call('Connect', 'POST');
        if (r.data) renderStatus(r.data); else notice(r.message);
        button.disabled = false; groups = null;
    };
    byId('waDisconnect').onclick = async e => {
        const button = e.currentTarget;
        if (!await confirmDialog('Disconnect the WhatsApp number? Documents will stop going to the group until the number is linked again.', { danger: true, confirmText: 'Disconnect' })) return;
        button.disabled = true; notice('');
        const r = await call('Disconnect', 'POST');
        if (r.data) renderStatus(r.data); else notice(r.message);
        button.disabled = false; groups = null; fillGroups();
    };

    // ---- destinations ----
    async function loadGroups() {
        groups = [];
        const r = await call('Groups');
        if (r.ok && r.data?.groups) groups = r.data.groups; else { groups = null; notice(r.message || 'The group list could not be loaded.'); return; }
        fillGroups();
    }
    function fillGroups() {
        document.querySelectorAll('#waDestinations select[data-group]').forEach(select => {
            const d = destinations.find(x => x.documentType === select.dataset.group);
            const keep = select.value || d?.groupId || '';
            const list = (groups || []).slice();
            if (keep && !list.some(g => g.id === keep) && d?.groupId === keep) list.unshift({ id: keep, name: d.groupName || keep, members: 0 });
            select.innerHTML = '<option value="">' + (groups === null ? 'Connect the number to see its groups' : 'No group (sending off)') + '</option>'
                + list.map(g => `<option value="${esc(g.id)}" data-name="${esc(g.name)}">${esc(g.name)}${g.members ? ' (' + g.members + ' members)' : ''}</option>`).join('');
            select.value = keep;
        });
    }
    async function loadDestinations() {
        const r = await call('Destinations');
        destinations = Array.isArray(r.data) ? r.data : [];
        const body = document.querySelector('#waDestinations tbody');
        body.innerHTML = destinations.map(d => `<tr data-type="${esc(d.documentType)}"><td>${esc(d.label)}</td>
            <td><select data-group="${esc(d.documentType)}" aria-label="Group for ${esc(d.label)}"></select></td>
            <td><div class="wa-row-actions"><button type="button" class="gp-button" data-save>Save</button><button type="button" class="gp-button gp-secondary" data-test>Send test message</button></div></td></tr>`).join('')
            || '<tr><td colspan="3">' + esc(r.message || 'Nothing to set up.') + '</td></tr>';
        fillGroups();
    }
    byId('waDestinations').addEventListener('click', async event => {
        const button = event.target.closest('[data-save],[data-test]'); if (!button || button.disabled) return;
        const row = button.closest('tr'), type = row.dataset.type, select = row.querySelector('select');
        const label = button.textContent; button.disabled = true; notice('');
        try {
            if (button.hasAttribute('data-save')) {
                const option = select.selectedOptions[0];
                const r = await call('SaveDestination', 'POST', { documentType: type, groupId: select.value, groupName: option?.dataset.name || '' });
                if (!r.ok) { await alertDialog(r.message || 'It could not be saved.', { type: 'error' }); return; }
                await loadDestinations(); await alertDialog('Saved.', { type: 'success' });
            } else {
                button.textContent = 'Sending…';
                const r = await call('SendTest', 'POST', { documentType: type });
                await alertDialog(r.message || (r.ok ? 'Sent.' : 'It could not be sent.'), { type: r.ok ? 'success' : 'error' });
                loadLog();
            }
        } finally { button.disabled = false; button.textContent = label; }
    });

    // ---- log ----
    async function loadLog() {
        const r = await call('Log'); const body = document.querySelector('#waLog tbody');
        const rows = Array.isArray(r.data) ? r.data : [];
        body.innerHTML = rows.length ? rows.map(l => `<tr><td>${esc(new Date(l.createdUtc + (l.createdUtc.endsWith('Z') ? '' : 'Z')).toLocaleString())}</td><td>${esc(l.documentNumber || l.documentType)}</td>
            <td>${l.status === 'Sent' ? '<span class="wa-sent">Sent</span>' : '<span class="wa-failed">Failed</span>' + (l.error ? '<span class="wa-reason">' + esc(l.error) + '</span>' : '')}</td><td>${esc(l.sentBy || '')}</td></tr>`).join('')
            : '<tr><td colspan="4">Nothing sent yet.</td></tr>';
    }

    loadDestinations(); refreshStatus(); loadLog();
    setInterval(refreshStatus, 5000); setInterval(loadLog, 30000);
})();
