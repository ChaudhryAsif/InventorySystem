// PrintTab.open(): shows the current page content in a new tab and prints it there, so the report stays open.
(function () {
    // The page's print-only rules, re-issued for every media so the new tab looks like the printed sheet.
    function printRulesForScreen() {
        let css = '';
        for (const sheet of document.styleSheets) {
            let rules;
            try { rules = sheet.cssRules; } catch { continue; }
            for (const r of rules)
                if (r instanceof CSSMediaRule && /\bprint\b/.test(r.media.mediaText) && !/\bnot\b/.test(r.media.mediaText))
                    css += `@media all{${[...r.cssRules].map(x => x.cssText).join('')}}`;
        }
        return css;
    }
    window.PrintTab = {
        autoPrint: true,
        open(selector) {
            const source = document.querySelector(selector || '#mainContent');
            const win = source ? window.open('', '_blank') : null;
            if (!win) { window.print(); return; }
            const copy = source.cloneNode(true);
            copy.querySelectorAll('script').forEach(s => s.remove());
            const stamp = copy.querySelector('#generatedDate');
            if (stamp) stamp.textContent = new Date().toLocaleString();
            const styles = [...document.querySelectorAll('link[rel="stylesheet"], style')]
                .map(n => n.tagName === 'LINK' ? `<link rel="stylesheet" href="${n.href}">` : n.outerHTML).join('');
            win.document.open();
            win.document.write(`<!DOCTYPE html><html><head><meta charset="utf-8"><title>${document.title}</title>${styles}
                <style>${printRulesForScreen()}</style>
                <style>body{display:block;margin:0;padding:1rem;background:#fff}.pt-actions{text-align:center;margin-bottom:1rem}.pt-actions button{padding:.5rem 1.2rem;border:none;border-radius:8px;background:#1e3a5f;color:#fff;cursor:pointer;font-size:.9rem}@media print{.pt-actions{display:none}body{padding:0}}</style></head>
                <body><div class="pt-actions"><button onclick="window.print()">🖨 Print</button> <button style="background:#64748b" onclick="window.close()">Close</button></div>
                <main class="main-wrapper" id="mainContent" style="margin:0;max-width:none;width:auto">${copy.innerHTML}</main></body></html>`);
            win.document.close();
            if (window.PrintTab.autoPrint) win.addEventListener('load', () => setTimeout(() => win.print(), 300));
        }
    };
})();
