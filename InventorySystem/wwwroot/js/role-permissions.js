// Permission grid: tick whole rows or columns, presets, copy from another role, and keep each module consistent
// (a write right needs View; "Edit saved" needs Edit). The server applies the same rules when it saves.
(function () {
    const grid = document.getElementById('permGrid'); if (!grid) return;
    const form = document.getElementById('permForm');
    const rows = [...grid.querySelectorAll('tbody tr')];
    const boxes = () => [...grid.querySelectorAll('input[name=permissions]')];
    const cell = (row, verb) => row.querySelector(`input[data-verb="${verb}"]`);
    const initial = new Set(boxes().filter(b => b.checked).map(b => b.value));
    let dirty = false;

    // Make one module consistent after a change; `changed` is the box the user just touched.
    function settle(row, changed) {
        const view = cell(row, 'View');
        const writes = [...row.querySelectorAll('input[name=permissions]')].filter(b => b !== view);
        if (changed === view && view && !view.checked) writes.forEach(b => b.checked = false);          // clearing View clears the rest
        else if (writes.some(b => b.checked) && view) view.checked = true;                                // any write right brings View
        const edit = cell(row, 'Edit'), saved = cell(row, 'EditSaved');
        if (saved && saved.checked && edit && !edit.checked) { if (changed === edit) saved.checked = false; else edit.checked = true; }   // Edit saved needs Edit
        if (changed === edit && edit && !edit.checked && saved) saved.checked = false;
        const all = [...row.querySelectorAll('input[name=permissions]')];
        const toggle = row.querySelector('.row-toggle'); if (toggle) toggle.checked = all.length > 0 && all.every(b => b.checked);
    }
    function refresh() {
        const on = boxes().filter(b => b.checked);
        document.getElementById('permCount').textContent = on.length + ' permission' + (on.length === 1 ? '' : 's') + ' selected';
        dirty = on.length !== initial.size || on.some(b => !initial.has(b.value));
        grid.querySelectorAll('.col-toggle').forEach(t => {
            const col = boxes().filter(b => b.dataset.verb === t.dataset.col);
            t.checked = col.length > 0 && col.every(b => b.checked); t.disabled = col.length === 0;
        });
    }
    function setAll(predicate) {
        rows.forEach(row => { row.querySelectorAll('input[name=permissions]').forEach(b => b.checked = predicate(b)); settle(row, null); });
        refresh();
    }

    grid.addEventListener('change', e => {
        const t = e.target;
        if (t.name === 'permissions') settle(t.closest('tr'), t);
        else if (t.classList.contains('row-toggle')) { t.closest('tr').querySelectorAll('input[name=permissions]').forEach(b => b.checked = t.checked); settle(t.closest('tr'), null); }
        else if (t.classList.contains('col-toggle')) rows.forEach(row => { const b = cell(row, t.dataset.col); if (b) { b.checked = t.checked; settle(row, b); } });
        refresh();
    });
    document.querySelectorAll('[data-preset]').forEach(button => button.addEventListener('click', () => {
        const preset = button.dataset.preset;
        setAll(b => preset === 'all' ? true : preset === 'view' ? b.dataset.verb === 'View' : false);
    }));
    const copyFrom = document.getElementById('copyFrom');
    if (copyFrom) {
        const roles = JSON.parse(grid.dataset.copy || '[]');
        copyFrom.addEventListener('change', () => {
            const role = roles.find(r => String(r.id) === copyFrom.value); if (!role) return;
            const ids = new Set(role.ids.map(String)); setAll(b => ids.has(b.value)); copyFrom.value = '';
        });
    }
    form.addEventListener('submit', () => { dirty = false; });
    window.addEventListener('beforeunload', e => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
    rows.forEach(row => settle(row, null)); refresh(); dirty = false;
})();
