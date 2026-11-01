(() => {
    'use strict';
    window.escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const token = document.querySelector('meta[name="csrf-token"]')?.content;
    const multiBranchEnabled = document.querySelector('meta[name="multi-branch-enabled"]')?.content === 'true';
    const originalFetch = window.fetch.bind(window);
    const pages = new Map();
    const filters = new Map();
    const requestIds = new Map();
    function cancellationReason(title = 'Cancellation reason', confirmLabel = 'Confirm cancellation') {
        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.style.cssText = 'position:fixed;inset:0;z-index:20000;background:rgba(15,23,42,.55);display:flex;align-items:center;justify-content:center;padding:20px';
            const panel = document.createElement('form');
            panel.setAttribute('role', 'dialog');
            panel.setAttribute('aria-modal', 'true');
            panel.setAttribute('aria-labelledby', 'cancellationReasonTitle');
            panel.style.cssText = 'background:white;border-radius:14px;padding:24px;width:480px;max-width:100%;box-shadow:0 20px 50px #0004;color:#243b53';
            panel.innerHTML = '<h3 id="cancellationReasonTitle">Cancellation reason</h3><label for="documentCancellationReason">Reason (3–500 characters)</label><textarea id="documentCancellationReason" minlength="3" maxlength="500" required rows="3" style="display:block;width:100%;box-sizing:border-box;margin:12px 0;padding:10px;border:1px solid #cbd5e1;border-radius:6px"></textarea><p role="alert" style="color:#b91c1c"></p><div style="display:flex;justify-content:flex-end;gap:10px"><button type="button" class="btn btn-secondary">Keep document</button><button type="submit" class="btn btn-danger">Confirm cancellation</button></div>';
            const input = panel.querySelector('textarea');
            panel.querySelector('h3').textContent = title;
            panel.querySelector('button[type="submit"]').textContent = confirmLabel;
            const finish = value => { overlay.remove(); resolve(value); };
            panel.querySelector('button[type="button"]').onclick = () => finish(null);
            panel.onsubmit = e => {
                e.preventDefault();
                const value = input.value.trim();
                if (value.length < 3 || value.length > 500) {
                    panel.querySelector('[role="alert"]').textContent = 'Enter a reason of 3–500 characters.';
                    input.focus(); return;
                }
                finish(value);
            };
            panel.onkeydown = e => { if (e.key === 'Escape') { e.preventDefault(); finish(null); } };
            overlay.append(panel); document.body.append(overlay); input.focus();
        });
    }
    window.requestOperationReason = cancellationReason;
    const listApis = {
        '/Sale/GetSales': 'loadSales', '/Purchase/GetPurchases': 'loadPurchases',
        '/SaleReturn/GetSaleReturns': 'loadReturns', '/PurchaseReturn/GetPurchaseReturns': 'loadReturns',
        '/Item/GetList': 'loadItemList', '/Parties/GetParties': 'loadParties', '/Item/GetItems': 'loadItems'
    };
    window.fetch = async (input, options = {}) => {
        const url = new URL(input instanceof Request ? input.url : input, location.href);
        if (url.origin !== location.origin) return originalFetch(input, options);
        const clientItemLookup = url.pathname === '/Item/GetItems' && document.getElementById('itemModal')?.dataset.itemPaging === 'client';
        const method = String(options.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
        const headers = new Headers(options.headers || (input instanceof Request ? input.headers : undefined));
        const branch = Number(document.getElementById('posBranch')?.value || 1);
        if (url.pathname === '/Item/GetItems' || url.pathname === '/Sale/GetStockByItem') url.searchParams.set('branchId', branch);
        if (url.pathname === '/Item/GetItems' && !clientItemLookup && !url.searchParams.has('search')) {
            const search = document.getElementById('itemSearchInput')?.value;
            if (search) url.searchParams.set('search', search);
        }
        let body = options.body;
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
            if (token) headers.set('X-CSRF-TOKEN', token);
            if (/\/(Sale|Purchase|SaleReturn|PurchaseReturn)\/Delete$/.test(url.pathname)) {
                const reason = await cancellationReason();
                if (!reason || reason.trim().length < 3 || reason.length > 500) return new Response(JSON.stringify({success:false,message:'Cancellation was not submitted. Enter a reason of 3–500 characters.'}), {headers:{'Content-Type':'application/json'}});
                headers.set('X-Operation-Reason', reason.trim());
            }
            if (typeof body === 'string' && headers.get('Content-Type')?.includes('application/json')) {
                const key = method + url.pathname + body;
                if (!requestIds.has(key)) requestIds.set(key, crypto.randomUUID());
                headers.set('X-Request-ID', requestIds.get(key));
            }
        }
        if (listApis[url.pathname] && !clientItemLookup) {
            const filterKey = url.searchParams.toString();
            if (filters.get(url.pathname) !== filterKey) pages.set(url.pathname, 1);
            filters.set(url.pathname, filterKey);
            url.searchParams.set('page', pages.get(url.pathname) || 1);
        }
        const response = await originalFetch(input instanceof Request ? new Request(url, input) : url, {...options, headers, body});
        if (!response.ok && [401, 403].includes(response.status)) {
            const message = response.status === 401 ? 'Your session expired. Please sign in again.' : (multiBranchEnabled ? 'You do not have permission for this action or branch.' : 'You do not have permission for this action.');
            if (window.alertDialog) window.alertDialog(message, {type:'warning'});
        }
        if (response.ok && !['GET', 'HEAD'].includes(method) && typeof body === 'string') {
            try { const data = await response.clone().json(); if (data.success !== false) requestIds.delete(method + url.pathname + body); } catch {}
        }
        if (response.ok && listApis[url.pathname] && !clientItemLookup) {
            try {
                const data = await response.clone().json();
                const page = data.page || 1, size = data.pageSize || 50, count = data.totalCount || 0;
                const main = url.pathname === '/Item/GetItems' ? document.getElementById('itemModal') : document.querySelector('main') || document.getElementById('mainContent');
                if (main) {
                    const pagerId = url.pathname === '/Item/GetItems' ? 'posItemPager' : 'posPager';
                    let pager = document.getElementById(pagerId);
                    if (!pager) { pager = document.createElement('div'); pager.id = pagerId; main.append(pager); }
                    pager.replaceChildren();
                    const previous = document.createElement('button'), next = document.createElement('button'), label = document.createElement('span');
                    previous.textContent = 'Previous'; next.textContent = 'Next';
                    previous.disabled = page <= 1; next.disabled = page * size >= count;
                    label.textContent = ` Page ${page} of ${Math.max(1, Math.ceil(count / size))} · ${count} records `;
                    const reload = n => { pages.set(url.pathname, n); const fn = window[listApis[url.pathname]]; if (typeof fn === 'function') fn(); };
                    previous.onclick = () => reload(page - 1); next.onclick = () => reload(page + 1);
                    pager.append(previous, label, next);
                }
            } catch {}
        }
        return response;
    };
    if (window.jQuery && token) jQuery.ajaxPrefilter((options, original, xhr) => {
        const url = new URL(options.url, location.href);
        if (url.origin === location.origin && !['GET', 'HEAD'].includes(String(options.type).toUpperCase())) xhr.setRequestHeader('X-CSRF-TOKEN', token);
    });
    // Prevent accidental loss of invoice data without introducing offline storage.
    document.addEventListener('DOMContentLoaded', () => {
        const branch = document.getElementById('posBranch');
        if (multiBranchEnabled && branch?.tagName === 'SELECT') {
            const selected = sessionStorage.getItem('posBranch');
            if ([...branch.options].some(o => o.value === selected)) branch.value = selected;
            branch.addEventListener('change', () => { sessionStorage.setItem('posBranch', branch.value); location.reload(); });
        }
        const form = document.querySelector('#saleForm, #purchaseForm, #saleReturnForm, #purchaseReturnForm');
        if (!form) return;
        let dirty = false;
        form.addEventListener('input', () => { dirty = true; });
        window.posMarkSaved = () => { dirty = false; };
        const search = document.getElementById('itemSearchInput');
        let timer;
        if (search && document.getElementById('itemModal')?.dataset.itemPaging !== 'client') search.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => { if (typeof window.loadItems === 'function') window.loadItems(); }, 250); });
        window.addEventListener('beforeunload', e => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
        document.addEventListener('click', async e => {
            const link = e.target.closest('a[href]');
            if (!dirty || !link || link.target === '_blank' || link.getAttribute('href').startsWith('#')) return;
            e.preventDefault();
            if (await confirmDialog('Leave this page and discard the unsaved invoice?')) { dirty = false; location.href = link.href; }
        });
        let busy = false;
        form.addEventListener('submit', e => {
            if (busy) { e.preventDefault(); e.stopImmediatePropagation(); return; }
            // Invoice handlers validate first; the fetch boundary locks actual requests.
        }, true);
        const fetchWithSafety = window.fetch;
        window.fetch = async (input, options = {}) => {
            const url = new URL(input instanceof Request ? input.url : input, location.href);
            const save = options.method === 'POST' && /\/(Save|Update)$/.test(url.pathname);
            if (!save) return fetchWithSafety(input, options);
            if (busy) throw new Error('An invoice save is already in progress.');
            busy = true;
            const buttons = form.querySelectorAll('button[type="submit"]'); buttons.forEach(b => b.disabled = true);
            try {
                const response = await fetchWithSafety(input, options);
                if (response.ok) { try { if ((await response.clone().json()).success) dirty = false; } catch {} }
                return response;
            } finally { busy = false; buttons.forEach(b => b.disabled = false); }
        };
    });
    document.addEventListener('error', e => {
        if (e.target instanceof HTMLImageElement && !e.target.dataset.fallback) {
            e.target.dataset.fallback = '1'; e.target.src = '/images/item-placeholder.svg';
        }
    }, true);
})();
