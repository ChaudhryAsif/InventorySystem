// Reusable custom confirmation modal — drop-in replacement for window.confirm().
// Usage: if (!(await confirmDialog('Delete this party?'))) return;
//        if (!(await confirmDialog('Delete this party?', { title: 'Remove Party', danger: true }))) return;
(function () {
    let backdrop, box, titleEl, msgEl, cancelBtn, confirmBtn, resolveFn;
    let stylesInjected = false;

    // Shared CSS for both the confirm and alert modals. Injected once, unconditionally,
    // regardless of which dialog function gets called first on a given page.
    function ensureStylesInjected() {
        if (stylesInjected) return;
        stylesInjected = true;

        const style = document.createElement('style');
        style.textContent = `
            .cdlg-backdrop { position: fixed; inset: 0; background: rgba(15,23,42,.55); display: none;
                align-items: center; justify-content: center; z-index: 10000; padding: 1rem; }
            .cdlg-backdrop.show { display: flex; }
            .cdlg-box { background: #fff; border-radius: 12px; width: min(420px, 100%); box-shadow: 0 20px 50px -12px rgba(0,0,0,.35);
                overflow: hidden; font-family: inherit; animation: cdlg-in .15s ease-out; }
            @keyframes cdlg-in { from { opacity: 0; transform: translateY(6px) scale(.98); } to { opacity: 1; transform: none; } }
            .cdlg-icon { font-size: 1.7rem; line-height: 1; }
            .cdlg-head { display: flex; align-items: center; gap: .7rem; padding: 1.35rem 1.5rem .5rem; }
            .cdlg-title { margin: 0; font-size: 1.08rem; font-weight: 700; color: #0f172a; }
            .cdlg-msg { padding: .3rem 1.5rem 1.4rem; margin: 0; color: #475569; font-size: .92rem; line-height: 1.55; }
            .cdlg-actions { display: flex; justify-content: flex-end; gap: .6rem; padding: 1rem 1.5rem; background: #f8fafc; border-top: 1px solid #eef2f7; }
            .cdlg-btn { border: none; border-radius: 8px; padding: .55rem 1.1rem; font-size: .88rem; font-weight: 600; cursor: pointer; }
            .cdlg-btn-cancel { background: #e2e8f0; color: #334155; }
            .cdlg-btn-cancel:hover { background: #cbd5e1; }
            .cdlg-btn-confirm { background: #0f172a; color: #fff; }
            .cdlg-btn-confirm:hover { background: #1e293b; }
            .cdlg-btn-confirm.cdlg-danger { background: #dc2626; }
            .cdlg-btn-confirm.cdlg-danger:hover { background: #b91c1c; }
        `;
        document.head.appendChild(style);
    }

    function ensureBuilt() {
        if (backdrop) return;
        ensureStylesInjected();

        backdrop = document.createElement('div');
        backdrop.className = 'cdlg-backdrop';
        backdrop.innerHTML = `
            <div class="cdlg-box" role="alertdialog" aria-modal="true">
                <div class="cdlg-head">
                    <span class="cdlg-icon" id="cdlgIcon">❓</span>
                    <h3 class="cdlg-title" id="cdlgTitle">Confirm</h3>
                </div>
                <p class="cdlg-msg" id="cdlgMsg"></p>
                <div class="cdlg-actions">
                    <button type="button" class="cdlg-btn cdlg-btn-cancel" id="cdlgCancel">Cancel</button>
                    <button type="button" class="cdlg-btn cdlg-btn-confirm" id="cdlgConfirm">OK</button>
                </div>
            </div>`;
        document.body.appendChild(backdrop);

        box = backdrop.querySelector('.cdlg-box');
        titleEl = backdrop.querySelector('#cdlgTitle');
        msgEl = backdrop.querySelector('#cdlgMsg');
        cancelBtn = backdrop.querySelector('#cdlgCancel');
        confirmBtn = backdrop.querySelector('#cdlgConfirm');

        const finish = (result) => {
            backdrop.classList.remove('show');
            document.removeEventListener('keydown', onKeydown);
            if (resolveFn) { const r = resolveFn; resolveFn = null; r(result); }
        };
        const onKeydown = (e) => {
            if (e.key === 'Escape') finish(false);
            if (e.key === 'Enter') finish(true);
        };

        cancelBtn.addEventListener('click', () => finish(false));
        confirmBtn.addEventListener('click', () => finish(true));
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) finish(false); });
        backdrop._finish = finish;
        backdrop._onKeydown = onKeydown;
    }

    window.confirmDialog = function (message, options) {
        options = options || {};
        ensureBuilt();

        titleEl.textContent = options.title || 'Confirm';
        msgEl.textContent = message || 'Are you sure?';
        cancelBtn.textContent = options.cancelText || 'Cancel';
        confirmBtn.textContent = options.confirmText || (options.danger ? 'Delete' : 'OK');
        confirmBtn.classList.toggle('cdlg-danger', !!options.danger);
        backdrop.querySelector('#cdlgIcon').textContent = options.danger ? '🗑️' : '❓';

        backdrop.classList.add('show');
        document.addEventListener('keydown', backdrop._onKeydown);

        return new Promise((resolve) => {
            resolveFn = resolve;
            confirmBtn.focus();
        });
    };

    // ── Alert modal — drop-in replacement for window.alert() ───────────────────
    // Usage: await alertDialog('Party updated successfully!');
    //        await alertDialog('❌ Error saving party.', { type: 'error' });
    //        await alertDialog('Full name is required!', { type: 'warning' });
    let abackdrop, atitleEl, amsgEl, aokBtn, aresolveFn;

    const ALERT_TYPES = {
        success: { icon: '✅', color: '#059669', title: 'Success' },
        error:   { icon: '❌', color: '#dc2626', title: 'Error' },
        warning: { icon: '⚠️', color: '#d97706', title: 'Warning' },
        info:    { icon: 'ℹ️', color: '#0f172a', title: 'Notice' }
    };

    // Infer type from a leading emoji already used in the message text, so
    // existing "✅ Saved!" / "❌ Error: ..." call sites colour correctly with no changes needed.
    function inferAlertType(message) {
        const m = (message || '').trim();
        if (m.startsWith('✅')) return 'success';
        if (m.startsWith('❌')) return 'error';
        if (m.startsWith('⚠️') || m.startsWith('⚠')) return 'warning';
        return 'info';
    }

    function ensureAlertBuilt() {
        if (abackdrop) return;
        ensureStylesInjected();

        abackdrop = document.createElement('div');
        abackdrop.className = 'cdlg-backdrop';
        abackdrop.innerHTML = `
            <div class="cdlg-box" role="alertdialog" aria-modal="true">
                <div class="cdlg-head">
                    <span class="cdlg-icon" id="adlgIcon">ℹ️</span>
                    <h3 class="cdlg-title" id="adlgTitle">Notice</h3>
                </div>
                <p class="cdlg-msg" id="adlgMsg"></p>
                <div class="cdlg-actions">
                    <button type="button" class="cdlg-btn cdlg-btn-confirm" id="adlgOk">OK</button>
                </div>
            </div>`;
        document.body.appendChild(abackdrop);

        atitleEl = abackdrop.querySelector('#adlgTitle');
        amsgEl = abackdrop.querySelector('#adlgMsg');
        aokBtn = abackdrop.querySelector('#adlgOk');

        const finish = () => {
            abackdrop.classList.remove('show');
            document.removeEventListener('keydown', onKeydown);
            if (aresolveFn) { const r = aresolveFn; aresolveFn = null; r(); }
        };
        const onKeydown = (e) => {
            if (e.key === 'Escape' || e.key === 'Enter') finish();
        };

        aokBtn.addEventListener('click', finish);
        abackdrop.addEventListener('click', (e) => { if (e.target === abackdrop) finish(); });
        abackdrop._finish = finish;
        abackdrop._onKeydown = onKeydown;
    }

    window.alertDialog = function (message, options) {
        options = options || {};
        ensureAlertBuilt();

        const type = options.type || inferAlertType(message);
        const meta = ALERT_TYPES[type] || ALERT_TYPES.info;

        atitleEl.textContent = options.title || meta.title;
        atitleEl.style.color = meta.color;
        amsgEl.textContent = message || '';
        aokBtn.textContent = options.okText || 'OK';
        aokBtn.style.background = meta.color;
        abackdrop.querySelector('#adlgIcon').textContent = meta.icon;

        abackdrop.classList.add('show');
        document.addEventListener('keydown', abackdrop._onKeydown);

        return new Promise((resolve) => {
            aresolveFn = resolve;
            aokBtn.focus();
        });
    };
})();
