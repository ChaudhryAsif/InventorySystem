// Shared helpers for report filters: URL state, "filters applied" summary, quick dates.
window.RptFilters = (() => {
    let ids = [];
    const el = id => document.getElementById(id);
    const iso = d => {
        const z = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
        return z.toISOString().split('T')[0];
    };

    function init(fieldIds) { ids = fieldIds; }

    // Fill fields from the page address; call after dropdown options have loaded.
    function loadFromUrl() {
        const p = new URLSearchParams(location.search);
        ids.forEach(id => { if (p.has(id) && el(id)) el(id).value = p.get(id); });
        const advanced = document.querySelector('.rpt-advanced');
        if (advanced && [...advanced.querySelectorAll('input,select')].some(i => i.value !== '' && p.has(i.id))) advanced.open = true;
    }

    // Keep non-empty filters in the address so the view can be bookmarked or shared.
    function saveToUrl() {
        const p = new URLSearchParams();
        ids.forEach(id => { const e = el(id); if (e && e.value !== '' && !(e.dataset.default !== undefined && e.value === e.dataset.default)) p.set(id, e.value); });
        history.replaceState(null, '', location.pathname + (p.toString() ? '?' + p : ''));
    }

    function labelOf(id) {
        const e = el(id);
        const group = e && e.closest('.rpt-filter-group');
        return (e && e.dataset.label) || group?.querySelector('.rpt-filter-label')?.textContent.trim() || id;
    }

    function valueOf(id) {
        const e = el(id);
        if (!e || e.value === '' || (e.dataset.default !== undefined && e.value === e.dataset.default)) return null;
        if (e.tagName === 'SELECT') return e.selectedOptions[0]?.textContent.trim() || e.value;
        return e.value;
    }

    // Write "Label: value" pairs for every filled filter into the summary box.
    function writeSummary(targetId) {
        const parts = ids.map(id => { const v = valueOf(id); return v === null ? null : `<strong>${esc(labelOf(id))}:</strong> ${esc(v)}`; }).filter(Boolean);
        const box = el(targetId);
        if (box) box.innerHTML = parts.length ? 'Filters applied: ' + parts.join(' · ') : 'No filters applied.';
    }

    function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

    function quickDate(kind, fromId, toId) {
        const t = new Date(), y = t.getFullYear(), m = t.getMonth();
        let from = t, to = t;
        if (kind === 'week') { const d = (t.getDay() + 6) % 7; from = new Date(y, m, t.getDate() - d); }
        else if (kind === 'month') from = new Date(y, m, 1);
        else if (kind === 'lastmonth') { from = new Date(y, m - 1, 1); to = new Date(y, m, 0); }
        else if (kind === 'year') from = new Date(y, 0, 1);
        el(fromId).value = iso(from);
        el(toId).value = iso(to);
    }

    return { init, loadFromUrl, saveToUrl, writeSummary, quickDate, iso };
})();
