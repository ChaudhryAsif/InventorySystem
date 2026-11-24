// GatePassSend: builds the receipt PDF in a hidden frame and sends it to the WhatsApp group through the server.
(function () {
    const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const headers = () => ({ 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'X-CSRF-TOKEN': csrf() });
    const sleep = ms => new Promise(r => setTimeout(r, ms));

    async function pdfBase64(id) {
        const frame = document.createElement('iframe');
        // Off screen but full size: a hidden or zero-size frame would draw the sheet wrongly.
        frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:820px;height:1200px;border:0;visibility:hidden';
        frame.src = '/GatePass/PdfFrame/' + id;
        document.body.appendChild(frame);
        try {
            let api = null;
            for (let i = 0; i < 80 && !api; i++) {            // up to 20 seconds for the page and the PDF library
                await sleep(250);
                const w = frame.contentWindow;
                try { if (w?.GPReceipt?.ready()) api = w.GPReceipt; } catch { }
            }
            if (!api) throw new Error('The receipt could not be prepared. Check the internet connection and try again.');
            const blob = await api.blob();
            return await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(String(reader.result).split(',')[1]);
                reader.onerror = () => reject(new Error('The receipt file could not be read.'));
                reader.readAsDataURL(blob);
            });
        } finally { frame.remove(); }
    }

    // Returns { ok, skipped, alreadySent, sentUtc, message }. Never throws for a normal failure.
    async function send(id, resend) {
        let pdf;
        try { pdf = await pdfBase64(id); } catch (e) { return { ok: false, message: e.message }; }
        let response;
        try { response = await fetch('/GatePass/SendToGroup', { method: 'POST', headers: headers(), body: JSON.stringify({ id, pdfBase64: pdf, resend: !!resend }) }); }
        catch { return { ok: false, message: 'The server could not be reached.' }; }
        const text = await response.text();
        let value = null;
        try { value = text ? JSON.parse(text) : null; } catch { }
        if (response.status === 409 && value?.alreadySent) return { ok: false, alreadySent: true, sentUtc: value.sentUtc, message: value.message };
        if (!response.ok || !value || value.success === false)
            return { ok: false, message: value?.message || (response.status === 403 ? 'You do not have permission to send gate passes.' : 'The gate pass could not be sent to WhatsApp.') };
        return { ok: true, skipped: !!value.skipped, message: value.message, groupName: value.groupName };
    }

    // Whether passes go to a group, and whether the last send failed. Any failure here just means "off".
    async function info() {
        try {
            const response = await fetch('/GatePass/GroupSendInfo', { headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' } });
            return response.ok ? await response.json() : { enabled: false };
        } catch { return { enabled: false }; }
    }

    window.GatePassSend = { send, info };
})();
