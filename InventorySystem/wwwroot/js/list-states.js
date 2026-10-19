// Shared list messages use DOM text nodes so labels cannot inject HTML.
window.PosListState = {
    clear(body) { body.querySelectorAll('[data-list-state]').forEach(row => row.remove()); },
    show(body, options) {
        this.clear(body);
        const row = document.createElement('tr');
        row.dataset.listState = options.error ? 'error' : 'empty';
        const cell = document.createElement('td');
        cell.colSpan = options.columns;
        cell.className = 'pos-list-state-cell';
        const panel = document.createElement('div');
        panel.className = 'pos-list-state';
        panel.setAttribute('role', options.error ? 'alert' : 'status');
        const title = document.createElement('strong');
        title.className = 'pos-list-state-title'; title.textContent = options.title;
        const message = document.createElement('p'); message.textContent = options.message;
        panel.append(title, message);
        if (options.actionText && (options.onAction || options.href)) {
            const action = document.createElement(options.href ? 'a' : 'button');
            action.className = 'pos-list-state-action'; action.textContent = options.actionText;
            if (options.href) action.href = options.href;
            else { action.type = 'button'; action.addEventListener('click', options.onAction); }
            panel.appendChild(action);
        }
        cell.appendChild(panel); row.appendChild(cell); body.appendChild(row);
    }
};
