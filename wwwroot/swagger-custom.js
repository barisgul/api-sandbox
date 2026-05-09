(function () {
    'use strict';
    console.log("Swagger Custom Validation Loaded v4");

    const showError = (modal, schemeFragment, message) => {
        modal.querySelectorAll('h4').forEach(h4 => {
            if (!h4.textContent.includes(schemeFragment)) return;
            let container = h4.parentElement;
            // Walk up until we find a node that has buttons (the full scheme block)
            while (container && container !== modal) {
                if (container.querySelector('button')) break;
                container = container.parentElement;
            }
            if (!container || container === modal) container = h4.parentElement;
            const existing = container.querySelector('.copilot-auth-error');
            if (existing) existing.remove();
            const div = document.createElement('div');
            div.className = 'copilot-auth-error';
            div.style.cssText = 'background:rgba(249,213,218,1);padding:10px;margin:10px 0;border-radius:4px;font-size:12px;word-break:break-word';
            div.innerHTML = '<b>Auth Error</b>&nbsp;&nbsp;' + message;
            container.appendChild(div);
        });
    };

    // Return the INNERMOST ancestor (below modal) that contains an h4 — gives us the specific scheme's block
    const findSchemeBlock = (el, modal) => {
        let node = el.parentElement;
        while (node && node !== modal) {
            const h4 = node.querySelector('h4');
            if (h4) return { block: node, title: h4.textContent };
            node = node.parentElement;
        }
        return null;
    };

    const doLogoutThenError = (modal, schemeFragment, message) => {
        setTimeout(() => {
            modal.querySelectorAll('h4').forEach(h4 => {
                if (!h4.textContent.includes(schemeFragment)) return;
                // Walk up from h4 until we find a container with a Logout button
                let node = h4.parentElement;
                while (node && node !== modal) {
                    const logoutBtn = Array.from(node.querySelectorAll('button'))
                        .find(b => b.textContent.trim() === 'Logout');
                    if (logoutBtn) {
                        logoutBtn.click();
                        setTimeout(() => showError(modal, schemeFragment, message), 200);
                        return;
                    }
                    node = node.parentElement;
                }
            });
        }, 100);
    };

    // CAPTURE phase (true) fires before Swagger UI can call stopPropagation
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('button');
        if (!btn) return;

        const btnText = btn.textContent.trim();
        console.log('[SwaggerValidation] button clicked:', JSON.stringify(btnText));

        // Swagger UI button text may contain icon chars — use includes
        if (!btnText.includes('Authorize')) return;

        const modal = document.querySelector('.modal-ux');
        if (!modal || !modal.contains(btn)) return;

        const found = findSchemeBlock(btn, modal);
        console.log('[SwaggerValidation] scheme found:', found ? found.title : 'NULL');
        if (!found) return;

        const { block, title } = found;

        if (title.includes('Basic')) {
            // Swagger UI renders username as a plain text input, password as type=password
            const user = (block.querySelector('input[type="text"]') || block.querySelector('input:not([type="password"])')).value || '';
            const pass = (block.querySelector('input[type="password"]') || {}).value || '';
            console.log('[SwaggerValidation] Basic - user:', user, 'pass length:', pass.length);
            // Skip if fields are empty (form was just reset after a logout)
            if (!user && !pass) return;
            const fd = new FormData();
            fd.append('username', user);
            fd.append('password', pass);
            const res = await fetch('/api/validate/basic', { method: 'POST', body: fd });
            console.log('[SwaggerValidation] Basic result:', res.status);
            if (!res.ok) {
                doLogoutThenError(modal, 'Basic',
                    'Error: Unauthorized, error: invalid_credentials, description: Invalid username or password.');
            }
        }

        if (title.includes('ApiKey')) {
            const key = (block.querySelector('input[type="text"]') || block.querySelector('input') || {}).value || '';
            if (!key) return;
            let location = 'header';
            let fragment = 'ApiKeyHeader';
            if (title.includes('ApiKeyQuery'))  { location = 'query';  fragment = 'ApiKeyQuery'; }
            if (title.includes('ApiKeyCookie')) { location = 'cookie'; fragment = 'ApiKeyCookie'; }
            const fd = new FormData();
            fd.append('key', key);
            fd.append('location', location);
            const res = await fetch('/api/validate/apikey', { method: 'POST', body: fd });
            console.log('[SwaggerValidation] ApiKey result:', res.status);
            if (!res.ok) {
                doLogoutThenError(modal, fragment,
                    'Error: Unauthorized, error: invalid_api_key, description: Invalid API key.');
            }
        }

        if (title.includes('Bearer') && !title.includes('Basic')) {
            const token = (block.querySelector('input[type="text"]') || block.querySelector('textarea') || {}).value || '';
            if (!token) return;
            const fd = new FormData();
            fd.append('token', token);
            const res = await fetch('/api/validate/bearer', { method: 'POST', body: fd });
            console.log('[SwaggerValidation] Bearer result:', res.status);
            if (!res.ok) {
                doLogoutThenError(modal, 'Bearer',
                    'Error: Unauthorized, error: invalid_token, description: Invalid or expired JWT. Call POST /api/BearerAuth/token to get a valid token.');
            }
        }
    }, true); // capture phase
})();
