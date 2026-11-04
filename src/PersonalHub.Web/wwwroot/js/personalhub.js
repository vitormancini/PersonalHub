// Comportamentos pequenos das páginas renderizadas no servidor (SSR estático).
// Os eventos são delegados no document: continuam funcionando depois da navegação aprimorada do Blazor,
// que troca o conteúdo da página sem executar scripts de novo.

// Mostrar/ocultar senha: <button data-ph-password-toggle="id-do-input">.
document.addEventListener('click', (event) => {
    const toggle = event.target.closest('[data-ph-password-toggle]');
    if (!toggle) {
        return;
    }

    const input = document.getElementById(toggle.dataset.phPasswordToggle);
    if (!input) {
        return;
    }

    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    toggle.dataset.visible = String(show);
    toggle.setAttribute('aria-label', show ? 'Ocultar senha' : 'Mostrar senha');
});

// Formulário com data-ph-busy-form: ao enviar, desabilita o botão e mostra o estado de carregamento
// (data-ph-busy-text). Também evita envio duplo por toque repetido.
document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-ph-busy-form')) {
        return;
    }

    const button = form.querySelector('button[type="submit"][data-ph-busy-text]');
    if (button?.getAttribute('aria-busy') === 'true') {
        event.preventDefault();
        return;
    }

    // Volta a senha para type=password, para o gerenciador de senhas reconhecer o envio.
    form.querySelectorAll('[data-ph-password-toggle]').forEach((toggle) => {
        const input = document.getElementById(toggle.dataset.phPasswordToggle);
        if (input) {
            input.type = 'password';
        }
    });

    if (button) {
        const label = button.querySelector('.ph-btn-text');
        button.dataset.phIdleText = label.textContent;
        label.textContent = button.dataset.phBusyText;
        button.setAttribute('aria-busy', 'true');
        button.disabled = true;
    }
});

// Ao voltar pelo histórico (cache do browser), a página reaparece como foi deixada: reativa o botão.
window.addEventListener('pageshow', (event) => {
    if (!event.persisted) {
        return;
    }

    document.querySelectorAll('button[aria-busy="true"][data-ph-busy-text]').forEach((button) => {
        const label = button.querySelector('.ph-btn-text');
        label.textContent = button.dataset.phIdleText ?? label.textContent;
        button.removeAttribute('aria-busy');
        button.disabled = false;
    });
});

// Copiar texto: <button data-ph-copy="id-do-elemento" data-ph-copied-text="Link copiado">.
document.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-ph-copy]');
    if (!button) {
        return;
    }

    const source = document.getElementById(button.dataset.phCopy);
    if (!source) {
        return;
    }

    const text = source.textContent.trim();
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        // Sem permissão de clipboard (HTTP, navegador antigo): seleciona o texto para o usuário copiar.
        const range = document.createRange();
        range.selectNodeContents(source);
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        return;
    }

    const label = button.querySelector('.ph-btn-text');
    label.textContent = button.dataset.phCopiedText ?? 'Copiado';
});

// Regras de senha ao vivo: <ul data-ph-password-rules="id-do-input"> com <li data-rule="length|upper|lower|digit">.
// Só ajuda visual; as regras valem no servidor (Identity).
const passwordRules = {
    length: (value) => value.length >= 8,
    upper: (value) => /[A-Z]/.test(value),
    lower: (value) => /[a-z]/.test(value),
    digit: (value) => /[0-9]/.test(value)
};

document.addEventListener('input', (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !input.id) {
        return;
    }

    document.querySelectorAll(`[data-ph-password-rules="${CSS.escape(input.id)}"] [data-rule]`).forEach((item) => {
        const met = passwordRules[item.dataset.rule]?.(input.value) ?? false;
        item.dataset.met = String(met);
    });
});
