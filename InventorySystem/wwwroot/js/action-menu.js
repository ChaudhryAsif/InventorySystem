// Row action menu: AM.wrap(mainHtml, [itemHtml | '-']) returns markup; no toggle when there are no items.
(function () {
    const openMenu = () => document.querySelector('.am.open');
    const close = () => { const m = openMenu(); if (m) { m.classList.remove('open'); m.querySelector('.am-toggle').setAttribute('aria-expanded', 'false'); } };
    window.AM = {
        wrap(main, items) {
            const list = (items || []).filter(Boolean);
            while (list[0] === '-') list.shift();
            while (list[list.length - 1] === '-') list.pop();
            if (list.every(i => i === '-')) list.length = 0;
            const menu = list.length
                ? `<button type="button" class="am-toggle" aria-haspopup="menu" aria-expanded="false" title="More actions">&#8943;</button><div class="am-menu" role="menu">${list.map(i => i === '-' ? '<hr>' : i).join('')}</div>`
                : '';
            return `<div class="am">${main || ''}${menu}</div>`;
        }
    };
    document.addEventListener('click', e => {
        const toggle = e.target.closest('.am-toggle');
        if (!toggle) { close(); return; }
        const box = toggle.closest('.am'), wasOpen = box.classList.contains('open');
        close();
        if (wasOpen) return;
        box.classList.add('open');
        toggle.setAttribute('aria-expanded', 'true');
        const menu = box.querySelector('.am-menu'), r = toggle.getBoundingClientRect();
        const top = r.bottom + menu.offsetHeight + 8 > window.innerHeight ? r.top - menu.offsetHeight - 4 : r.bottom + 4;
        menu.style.top = Math.max(4, top) + 'px';
        menu.style.left = Math.max(4, r.right - menu.offsetWidth) + 'px';
    });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
    window.addEventListener('resize', close);
    window.addEventListener('scroll', close, true);
})();
