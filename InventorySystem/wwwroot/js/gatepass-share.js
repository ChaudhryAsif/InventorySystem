// GatePassShare.open(id, win): makes the customer receipt link and opens WhatsApp with the message ready.
// Pass a window opened during the click (window.open('', '_blank')) so the browser does not block it.
(function () {
    const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    window.GatePassShare = {
        async open(id, win) {
            const target = win || window.open('', '_blank');
            try {
                const response = await fetch('/GatePass/CreateShareLink', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', 'X-CSRF-TOKEN': csrf() },
                    body: JSON.stringify({ id })
                });
                const text = await response.text();
                let value = null;
                try { value = text ? JSON.parse(text) : null; } catch { }
                if (!response.ok || !value || value.success === false)
                    throw new Error(value?.message || (response.status === 403 ? 'You do not have permission to share this gate pass.' : 'The share link could not be created. Please refresh and try again.'));
                if (target) target.location.href = value.whatsappUrl; else location.href = value.whatsappUrl;
                return value;
            } catch (e) {
                if (target) target.close();
                throw e;
            }
        }
    };
})();
