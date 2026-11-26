// View-only users: hide the buttons that would add, change or delete data on the current page.
// The server decides what is allowed (every save, edit and delete is refused without its permission);
// this only keeps buttons that cannot work out of sight, so a missed one is harmless.
// <body data-deny="create edit delete post"> lists the verbs the user lacks on this page's module.
(function () {
    // Words a control's label starts with (after any icon or plus sign), per verb.
    const LABEL = {
        create: /^(new|add|create|import|upload|send)\b/,
        edit: /^(edit|update|approve|mark|reverse|complete|record|pay|receive|settle|reconcile|finali[sz]e|link)\b/,
        post: /^(post|approve|finali[sz]e)\b/,
        delete: /^(delete|remove|void)\b|^cancel (order|invoice|pass|voucher|sheet|entry)/
    };
    // Words inside an id, class, onclick, title or data-action of an icon-only button (editItem, btn-delete, data-action="void").
    const META = {
        create: /(^|[^a-z])(add|create|import|upload)/,
        edit: /(^|[^a-z])(edit|approve|reverse|markfinal|markcleared)/,
        post: /(^|[^a-z])(post|approve)/,
        delete: /(^|[^a-z])(delete|remove|void)/
    };
    const CONTROLS = 'button, a, input[type=button], input[type=submit], [role=button]';
    const SKIP = '.sidebar, .main-header, .cdlg-backdrop, .view-only-keep';

    function start() {
        const deny = (document.body.dataset.deny || '').split(' ').filter(Boolean);
        if (!deny.length) return;
        const lacks = verb => deny.includes(verb);

        function verbOf(el) {
            const raw = (el.tagName === 'INPUT' ? el.value : el.textContent) || '';
            const label = raw.replace(/\s+/g, ' ').trim().toLowerCase().replace(/^[^a-z]+/, '');
            const meta = [el.id, typeof el.className === 'string' ? el.className : '', el.getAttribute('onclick'), el.getAttribute('title'), el.getAttribute('aria-label'), el.getAttribute('data-action')].join(' ').toLowerCase();
            // Save is used for both new and changed records, so it goes only when the user may do neither.
            if (label.length <= 40 && /^save\b/.test(label)) return lacks('create') && lacks('edit') ? 'save' : null;
            for (const verb of Object.keys(LABEL))
                if (lacks(verb) && ((label.length <= 40 && LABEL[verb].test(label)) || (label.length <= 3 && META[verb].test(meta)))) return verb;
            return null;
        }

        const hide = el => { el.style.display = 'none'; el.setAttribute('data-view-only-hidden', 'true'); };
        function scan(root) {
            const nodes = root.matches?.(CONTROLS) ? [root] : [];
            root.querySelectorAll?.(CONTROLS).forEach(n => nodes.push(n));
            for (const el of nodes) {
                if (el.hasAttribute('data-view-only-hidden') || el.closest(SKIP)) continue;
                if (el.hasAttribute('data-write') || verbOf(el)) hide(el);
            }
        }

        // Rows and menus the page builds later are checked too.
        let queued = new Set(), frame = 0;
        new MutationObserver(records => {
            for (const r of records) for (const n of r.addedNodes) if (n.nodeType === 1) queued.add(n);
            if (!frame && queued.size) frame = requestAnimationFrame(() => { frame = 0; const batch = queued; queued = new Set(); batch.forEach(scan); });
        }).observe(document.body, { childList: true, subtree: true });
        scan(document.body);
    }

    // The script is loaded in the page head, so wait for the body.
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
