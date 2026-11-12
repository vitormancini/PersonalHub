# PersonalHub — Templates do WhatsApp

Textos prontos para cadastrar no **Gerenciador do WhatsApp > Modelos de mensagem** da Meta (D-055). O nome do template é exatamente o que o código envia (`NotificationTemplates`). Todos em **Português (BR)** (`pt_BR`). Nenhuma variável recebe texto livre digitado por usuário (D-060); o código troca quebras de linha e espaços repetidos por um espaço e corta em 60 caracteres.

**Botão de URL (todos os UTILITY):** tipo *Visitar site*, URL **dinâmica** `https://<App:PublicBaseUrl>/{{1}}`. O PersonalHub envia só o sufixo (coluna "Sufixo do botão"). Exemplo com `App:PublicBaseUrl = https://app.personalhub.com.br`: URL do template `https://app.personalhub.com.br/{{1}}`, exemplo do sufixo `aluno/aulas/3f2a9c1e-0000-0000-0000-000000000000`. Se o endereço público mudar, os templates precisam ser editados (e reaprovados).

Um template só pode ser usado depois de **aprovado** pela Meta. Enquanto não estiver, os envios falham com erro `132001` (template inexistente), que é definitivo: o aviso fica `Failed` (ver `LastError` na outbox).

## 1. `ph_codigo` — código de verificação

- Categoria: **AUTHENTICATION**
- Entrega do código: **Copiar código** (botão "Copiar código").
- Opções: marcar **"Adicionar recomendação de segurança"** e **"Adicionar tempo de expiração do código"** com **10 minutos**.
- O texto dessa categoria é gerado pela Meta (não é editável); com as opções acima fica parecido com:

```
*{{1}}* é seu código de verificação. Para sua segurança, não o compartilhe.
Este código expira em 10 minutos.
```

- Variável: `{{1}}` = código de 6 dígitos (exemplo `482913`). O código também vai no parâmetro do botão de copiar.
- Usado no aceite do convite (D-057), na confirmação do celular de alunos antigos (D-059) e na troca de senha (D-058). Sai mesmo no horário de silêncio.

## 2. Avisos para o aluno (UTILITY)

### `ph_nova_aula`
```
Olá, {{1}}! O {{2}} registrou uma aula em {{3}} às {{4}}. Confirme se ela aconteceu.
```
| Variável | Conteúdo | Exemplo |
|---|---|---|
| {{1}} | primeiro nome do aluno | Mariana |
| {{2}} | nome de exibição do personal | Carlos Lima |
| {{3}} | data da aula (dd/MM) | 12/11 |
| {{4}} | horário (HH:mm, Brasília) | 07:30 |

Botão: **Ver aula** — sufixo `aluno/aulas/{id da aula}`.

### `ph_aula_excluida`
```
Olá, {{1}}! O {{2}} excluiu a aula de {{3}} às {{4}}, que você já tinha respondido. O motivo está no PersonalHub.
```
Variáveis como em `ph_nova_aula`. Botão: **Ver aulas** — sufixo `aluno/aulas`. Só para aula já confirmada ou recusada (a pendente some para o aluno, D-042).

### `ph_demonstrativo_emitido`
```
Olá, {{1}}! Seu demonstrativo de {{2}} com o {{3}} está pronto: {{4}} aulas, total de {{5}}, vencimento em {{6}}.
```
| Variável | Conteúdo | Exemplo |
|---|---|---|
| {{1}} | primeiro nome do aluno | Mariana |
| {{2}} | mês de referência, minúsculo | outubro |
| {{3}} | nome do personal | Carlos Lima |
| {{4}} | quantidade de aulas cobradas | 8 |
| {{5}} | total (R$) | R$ 960,00 |
| {{6}} | vencimento (dd/MM) | 10/11 |

Botão: **Ver demonstrativo** — sufixo `aluno/pagamentos/{id do demonstrativo}`. Não é enviado para demonstrativo de total zero (já nasce pago, D-044).

### `ph_pagamento_confirmado`
```
Olá, {{1}}! O {{2}} confirmou o recebimento do pagamento de {{3}} ({{4}}).
```
{{1}} primeiro nome do aluno; {{2}} nome do personal; {{3}} mês (`outubro`); {{4}} valor (`R$ 960,00`). Botão: **Ver demonstrativo** — sufixo `aluno/pagamentos/{id}`. Também enviado quando o personal registra o pagamento direto (D-052).

### `ph_pagamento_nao_recebido`
```
Olá, {{1}}! O {{2}} ainda não recebeu o pagamento de {{3}} e deixou uma mensagem para você no PersonalHub.
```
{{1}} primeiro nome do aluno; {{2}} nome do personal; {{3}} mês (`outubro`). Botão: **Ver demonstrativo** — sufixo `aluno/pagamentos/{id}`. A mensagem do personal nunca vai no WhatsApp.

## 3. Avisos para o personal (UTILITY)

### `ph_aula_confirmada`
```
A {{1}} confirmou a aula de {{2}} às {{3}}.
```
{{1}} nome do aluno como o personal cadastrou (D-015, ex.: `Mariana Lopes`); {{2}} data (`12/11`); {{3}} horário (`07:30`). Botão: **Ver aulas** — sufixo `personal/aulas`.

### `ph_aula_recusada`
```
A {{1}} recusou a aula de {{2}} às {{3}}. Os detalhes estão no PersonalHub.
```
Variáveis como em `ph_aula_confirmada`. Botão: **Ver aula** — sufixo `personal/aulas/{id da aula}`. O motivo da recusa fica só no app.

### `ph_pagamento_informado`
```
A {{1}} informou o pagamento de {{2}} ({{3}}). Confira na sua conta e confirme o recebimento.
```
{{1}} nome do aluno (cadastro do personal); {{2}} mês (`outubro`); {{3}} valor (`R$ 960,00`). Botão: **Confirmar recebimento** — sufixo `personal/financeiro/{id do demonstrativo}`. A observação do aluno fica só no app.

## 4. Observações sobre os textos aprovados

- "O {{2}}" e "A {{1}}" supõem gênero. Se quiser neutralizar sem mudar o sentido, por exemplo: "{{2}} registrou uma aula…", "Mariana Lopes confirmou a aula…" (exige editar o template na Meta; o código não muda).
- `ph_pagamento_nao_recebido` diz que o personal "deixou uma mensagem", mas a mensagem do "Não recebi" é opcional (D-051). Alternativa: "…ainda não recebeu o pagamento de {{3}}. Veja os detalhes no PersonalHub."
- A Meta recusa variáveis no começo ou no fim do corpo e mais de 4 espaços seguidos. Os textos acima terminam com pontuação depois da variável; se a revisão reclamar, acrescente uma palavra depois dela.
