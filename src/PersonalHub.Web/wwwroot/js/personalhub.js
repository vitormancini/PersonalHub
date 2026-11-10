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

    // O botão que enviou (pode estar fora do <form>, ligado por form=). Um botão sem data-ph-busy-text (por exemplo,
    // uma sugestão que só preenche um campo) não bloqueia o formulário.
    const submitter = event.submitter;
    const button = submitter
        ? (submitter.matches('[data-ph-busy-text]') ? submitter : null)
        : form.querySelector('button[type="submit"][data-ph-busy-text]');
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
        // Desabilita só depois de o formulário ser montado: um botão desabilitado durante o evento sai dos dados
        // enviados (envio nativo e aprimorado do Blazor), e o name/value do botão (por exemplo, o id da aula em
        // "Confirmar" da lista do aluno) se perderia. O envio duplo já é barrado pela checagem de aria-busy acima.
        setTimeout(() => { button.disabled = true; }, 0);
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

// Registrar aula: o cartão do valor e o aviso de convite acompanham o aluno escolhido no
// <select data-ph-lesson-student>. Os textos vêm dos data-* de cada <option>, gerados pelo servidor.
document.addEventListener('change', (event) => {
    const select = event.target;
    if (!(select instanceof HTMLSelectElement) || !select.hasAttribute('data-ph-lesson-student')) {
        return;
    }

    const form = select.form;
    const option = select.selectedOptions[0];
    const chosen = Boolean(option?.value);
    form.querySelectorAll('[data-ph-lesson-info]').forEach((element) => { element.hidden = !chosen; });
    form.querySelectorAll('[data-ph-lesson-empty]').forEach((element) => { element.hidden = chosen; });
    form.querySelectorAll('[data-ph-lesson-invited]').forEach((element) => {
        element.hidden = !chosen || option.dataset.invited !== 'true';
    });
    if (!chosen) {
        return;
    }

    form.querySelectorAll('[data-ph-lesson-name]').forEach((element) => { element.textContent = option.dataset.name; });
    form.querySelectorAll('[data-ph-lesson-price]').forEach((element) => { element.textContent = option.dataset.price; });
});

// Radios opcionais em <div data-ph-toggle-radios>: tocar de novo no escolhido desmarca.
document.addEventListener('click', (event) => {
    const radio = event.target;
    if (!(radio instanceof HTMLInputElement) || radio.type !== 'radio' || !radio.closest('[data-ph-toggle-radios]')) {
        return;
    }

    if ((radio.dataset.phWasChecked ?? String(radio.defaultChecked)) === 'true') {
        radio.checked = false;
    }

    radio.closest('[data-ph-toggle-radios]').querySelectorAll('input[type="radio"]').forEach((other) => {
        other.dataset.phWasChecked = String(other.checked);
    });
});

// Sugestões que preenchem um campo: <button data-ph-fill="id-do-campo" data-ph-fill-value="texto">. Sem
// JavaScript o botão envia o formulário e o servidor preenche o campo.
document.addEventListener('click', (event) => {
    const button = event.target.closest('[data-ph-fill]');
    if (!button) {
        return;
    }

    const field = document.getElementById(button.dataset.phFill);
    if (!field) {
        return;
    }

    event.preventDefault();
    field.value = button.dataset.phFillValue;
    field.focus();
});
