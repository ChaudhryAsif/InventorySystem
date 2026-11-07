// Page-break helpers for the html2pdf report downloads: rows move whole to the next page instead of being sliced.
window.PdfPages = (function () {
    var style = document.createElement('style');
    style.textContent = '.pdf-exporting tr, .pdf-exporting .pdf-unit, .pdf-exporting .rpt-summary-card, .pdf-exporting .rpt-detail-container,' +
        '.pdf-exporting .summary-card, .pdf-exporting .ac-balance-card { break-inside: avoid; page-break-inside: avoid; }' +
        '.pdf-exporting .pdf-unit > tr:last-child { border-bottom: 1px solid #e5e7eb; }';
    document.head.appendChild(style);

    // Packs a heading row, its first body row and any detail rows into one block so a page break never separates them.
    function pack(tbody) {
        var table = tbody.parentNode, made = [], unit = null, closed = true;
        Array.prototype.filter.call(tbody.children, function (r) { return r.tagName === 'TR'; }).forEach(function (r) {
            var head = r.classList.contains('rpt-group-row') || r.classList.contains('pdf-head');
            var detail = r.classList.contains('rpt-detail-row');
            if (!unit || (closed && !detail)) {
                unit = document.createElement('tbody');
                unit.className = 'pdf-unit';
                table.insertBefore(unit, tbody);
                made.push(unit);
                closed = false;
            }
            unit.appendChild(r);
            if (!head && !detail) closed = true;
        });
        return function unpack() {
            made.forEach(function (u) { while (u.firstChild) tbody.appendChild(u.firstChild); u.remove(); });
        };
    }

    return { options: { mode: ['css'] }, pack: pack };
})();
