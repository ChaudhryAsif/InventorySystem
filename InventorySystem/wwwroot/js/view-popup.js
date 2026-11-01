// ViewPopup.open(title, viewUrl, printUrl): read-only popup; Print opens printUrl in a new tab.
(function () {
    let box, frame, titleEl, printUrl = '';
    function build() {
        box = document.createElement('div');
        box.className = 'vp';
        box.innerHTML = '<div class="vp-box" role="dialog" aria-modal="true"><div class="vp-bar"><span class="vp-title"></span>'
            + '<span class="vp-actions"><button type="button" class="vp-print">🖨 Print</button><button type="button" class="vp-close" aria-label="Close">&times;</button></span></div>'
            + '<iframe title="Details"></iframe></div>';
        document.body.appendChild(box);
        frame = box.querySelector('iframe');
        titleEl = box.querySelector('.vp-title');
        box.addEventListener('click', e => { if (e.target === box) close(); });
        box.querySelector('.vp-close').addEventListener('click', close);
        box.querySelector('.vp-print').addEventListener('click', () => { if (printUrl) window.open(printUrl, '_blank', 'noopener'); });
        document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
    }
    function close() {
        if (!box) return;
        box.classList.remove('open');
        frame.removeAttribute('src');
    }
    window.ViewPopup = {
        open(title, viewUrl, printLink) {
            if (!box) build();
            titleEl.textContent = title;
            printUrl = printLink;
            frame.src = viewUrl;
            box.classList.add('open');
        },
        close
    };
})();
