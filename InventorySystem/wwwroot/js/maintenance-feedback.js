/* Keep an open form intact when deployment interrupts a request. Never replay writes. */
(() => {
    'use strict';
    const fetchBeforeMaintenance = window.fetch.bind(window);
    let panel;
    function isUnavailable(status) { return [502, 503, 504].includes(status); }
    function showNotice(deployment) {
        if (panel) return;
        panel = document.createElement('div');
        panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
        panel.setAttribute('aria-labelledby', 'maintenanceNoticeTitle');
        panel.style.cssText = 'position:fixed;inset:0;z-index:2147483646;background:#173647bd;display:grid;place-items:center;padding:20px';
        const card = document.createElement('div');
        card.style.cssText = 'max-width:520px;background:white;border-radius:16px;padding:32px;color:#203c50;box-shadow:0 18px 60px #0003;font:16px/1.6 system-ui';
        const heading = document.createElement('h2'); heading.id = 'maintenanceNoticeTitle'; heading.tabIndex = -1;
        heading.textContent = deployment ? 'System Under Maintenance' : 'The system is temporarily unavailable';
        const text = document.createElement('p');
        text.textContent = (deployment ? 'We are performing scheduled maintenance. Please try again in a few minutes. ' : 'Please wait a few minutes and check again. ') + 'Keep this page open to retain your entered details.';
        const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Check again';
        button.style.cssText = 'border:0;border-radius:8px;background:#2d607c;color:white;padding:12px 22px;font:600 16px system-ui;cursor:pointer';
        button.onclick = async () => {
            if (button.dataset.ready === 'true') { panel.remove(); panel = null; return; }
            button.disabled = true;
            try {
                const result = await fetchBeforeMaintenance('/Auth/Login', { cache: 'no-store', credentials: 'same-origin' });
                if (result.ok && !(await result.text()).includes('name="pos-maintenance"')) {
                    heading.textContent = 'The system is available again';
                    text.textContent = 'Your entries are still on this page. Review them before submitting again. Your previous action has not been automatically repeated.';
                    button.dataset.ready = 'true'; button.textContent = 'Continue';
                } else text.textContent = 'The system is still unavailable. Please wait a few minutes and check again.';
            } catch { text.textContent = 'Unable to reconnect. Check your connection and try again.'; }
            finally { button.disabled = false; }
        };
        card.append(heading, text, button); panel.append(card); document.body.append(panel); heading.focus();
        panel.addEventListener('keydown', event => { if (event.key === 'Tab') { event.preventDefault(); button.focus(); } });
    }
    window.fetch = async (input, options) => {
        const response = await fetchBeforeMaintenance(input, options);
        const url = new URL(input instanceof Request ? input.url : input, location.href);
        if (url.origin === location.origin && isUnavailable(response.status)) {
            const body = await response.clone().text().catch(() => '');
            showNotice(body.includes('name="pos-maintenance"'));
        }
        return response;
    };
    if (window.jQuery) window.jQuery(document).ajaxError((_event, xhr, settings) => {
        if (new URL(settings.url, location.href).origin !== location.origin) return;
        if (isUnavailable(xhr.status)) showNotice(String(xhr.responseText || '').includes('name="pos-maintenance"'));
    });
})();
