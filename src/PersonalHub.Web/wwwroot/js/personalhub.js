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
