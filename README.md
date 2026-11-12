# PersonalHub

Aplicação web para personal trainers gerenciarem alunos, aulas e cobranças mensais. Os alunos acompanham e reconhecem suas aulas e informam pagamentos.

Stack: .NET 10, Blazor Web App (Interactive Server), PostgreSQL, Bootstrap. O código é organizado em Vertical Slice Architecture.

A documentação de produto, domínio, arquitetura, segurança, UX e roadmap fica em [`.claude/docs/`](.claude/docs/).

## Pré-requisitos

- .NET SDK 10 (versão fixada em `global.json`)
- Docker com Docker Compose v2

## Banco de dados local (PostgreSQL)

1. Crie o `.env` a partir do exemplo e ajuste a senha (o `.env` não é versionado):

   ```bash
   cp .env.example .env
   ```

2. Suba o PostgreSQL:

   ```bash
   docker compose up -d
   ```

3. Confira se o container está `healthy`:

   ```bash
   docker compose ps
   ```

Para parar sem perder dados: `docker compose down`.

Para **apagar todos os dados** e recomeçar do zero (remove o volume):

```bash
docker compose down -v
```

## Configurar a connection string (user secrets)

A connection string não fica no repositório. Use os mesmos valores do seu `.env`:

```bash
dotnet user-secrets set "ConnectionStrings:PersonalHub" "Host=localhost;Port=5432;Database=personalhub;Username=personalhub;Password=SUA_SENHA" --project src/PersonalHub.Web
```

## Banco de dados: migrations

As ferramentas do EF Core estão fixadas no manifesto `dotnet-tools.json`:

```bash
dotnet tool restore
dotnet ef database update --project src/PersonalHub.Web
```

As migrations ficam em `src/PersonalHub.Web/Infrastructure/Persistence/Migrations`. A aplicação **não** aplica migrations ao iniciar.

Para criar uma nova migration:

```bash
dotnet ef migrations add NomeDaMigration --project src/PersonalHub.Web --output-dir Infrastructure/Persistence/Migrations
```

## Executar a aplicação

```bash
dotnet run --project src/PersonalHub.Web
```

A URL aparece no terminal. O health check fica em `/health` e inclui a conexão com o PostgreSQL.

## Login e provisionamento de personal

A aplicação abre em `/entrar`. Após o login, o personal vai para `/personal` e o aluno para `/aluno`. Para sair, use o botão **Sair** (POST `/sair`).

Não há tela de cadastro de personal (D-027). Para criar um, rode o comando de provisionamento (o servidor web não sobe):

```bash
dotnet run --project src/PersonalHub.Web -- provision-personal --email ana@exemplo.com --name "Ana Souza" --phone +5511999990000
```

- `--phone` é opcional e usa o formato internacional (E.164).
- A senha é pedida duas vezes no terminal, sem aparecer na tela. Para automação, ela pode vir por pipe (uma linha). Nunca passe a senha como argumento.
- Regras: mínimo de 8 caracteres, com maiúscula, minúscula e número.
- Usuário, role `Personal`, `PersonalProfile` ativo e registro de auditoria são gravados numa única transação.
- Saída: código 0 (criado), 1 (dados ou senha recusados) ou 2 (uso incorreto).

Regras de acesso:
- Cookie `__Host-PersonalHub.Auth`: HttpOnly, Secure e SameSite=Lax. Use o perfil `https` (`dotnet run --project src/PersonalHub.Web --launch-profile https`): em HTTP puro o browser não guarda o cookie.
- "Manter conectado" mantém a sessão por 14 dias (renovada com o uso). Sem a opção, o cookie dura até fechar o browser.
- 5 senhas erradas seguidas bloqueiam a conta por 15 minutos.
- POST em `/entrar` é limitado a 10 por minuto por IP (`Authentication:LoginRateLimit`). Acima disso, a resposta é 429.
- Personal inativo não entra. A mensagem é a mesma de senha errada, que também não revela se o e-mail existe.

## Cadastro e convite do aluno

O personal cadastra o aluno em **Alunos → Novo aluno** (`/personal/alunos/novo`). O vínculo nasce `Invited` e o sistema gera um convite de uso único, válido por 7 dias (D-016):
- O link (`/convite/{token}`) aparece **uma única vez**, na página seguinte ao cadastro. O banco guarda só o SHA-256 do token. Recarregar a página não mostra o link de novo.
- O botão **Enviar pelo WhatsApp** abre o WhatsApp do próprio personal com a mensagem pronta (`wa.me`, D-031). O PersonalHub não envia nada sozinho.
- O aluno abre o link (nada é enviado só por abrir), toca em **Enviar código pelo WhatsApp**, digita o código de 6 números que chega no celular cadastrado pelo personal e só então cria a senha (ou entra com a senha atual, se o e-mail já tiver conta), aceita os termos e cai em `/aluno` (D-057). O celular passa a ser o confirmado da conta.
- Alunos que aceitaram antes da Fase 6 confirmam o celular em `/aluno/confirmar-celular` logo após o login (D-059).
- O caminho `/convite/*` é limitado a 20 requisições por minuto por IP (`Students:InviteRateLimit`), responde com `Referrer-Policy: no-referrer` e aparece nos logs como `/convite/[redacted]`.

## Demonstrativo e fechamento mensal

No dia do vencimento de cada aluno (a partir de 00:00 de Brasília), o app emite o demonstrativo do mês anterior com as aulas confirmadas e ainda não cobradas (D-043 a D-048). O personal vê em **Financeiro** (`/personal/financeiro`); o aluno, em **Pagamentos** (`/aluno/pagamentos`).

- O fechamento roda dentro do app (`BackgroundService`): na partida e a cada hora. Se o app ficou fora do ar no dia, a próxima execução recupera. Configuração: `Billing:Closing:Enabled` (padrão `true`) e `Billing:Closing:Interval` (padrão `01:00:00`).
- É idempotente e seguro com mais de uma instância (advisory lock do PostgreSQL + índices únicos).
- Para rodar uma vez, sem subir o servidor web:

```bash
dotnet run --project src/PersonalHub.Web -- close-billing
```

- Para simular o dia do vencimento em desenvolvimento (só com `ASPNETCORE_ENVIRONMENT=Development`), passe a data; o relógio fica em 06:00 de Brasília desse dia:

```bash
dotnet run --project src/PersonalHub.Web -- close-billing --date 2026-12-10
```

- Saída: código 0 (ok), 1 (falha em algum vínculo, ou outra execução em andamento) ou 2 (uso incorreto).

## Pagamentos informados

O aluno paga direto ao personal (Pix, dinheiro…) e avisa pelo app; o personal confere e confirma (D-049 a D-054). Sempre o valor total do demonstrativo, sem parcial (D-024).

- Aluno: **Já paguei** no demonstrativo (`/aluno/pagamentos/{id}`) → `/informar`, com observação opcional. O demonstrativo fica "Pagamento informado".
- Personal: **Pagamentos para confirmar** no topo do Financeiro (todos os meses) com **Confirmar** (direto) e **Não recebi** (`/personal/financeiro/{id}/nao-recebi`, mensagem opcional que o aluno vê). Recusado, o demonstrativo volta a "Aguardando pagamento" e o aluno pode informar de novo.
- Sem aviso do aluno (ex.: dinheiro), o personal usa **Registrar recebimento** no demonstrativo (`/registrar`).
- Confirmação e registro são definitivos. Cada transição trava o demonstrativo (`SELECT ... FOR UPDATE`) e grava `AuditLog` (`PaymentReported`, `PaymentConfirmed`, `PaymentRejected`, `PaymentRecorded`) sem os textos livres.
- Avisos por WhatsApp desses eventos: veja "WhatsApp" abaixo.

## WhatsApp

O PersonalHub envia por WhatsApp os códigos de verificação e os avisos de aulas, demonstrativo e pagamentos (D-055 a D-063). Os textos e variáveis dos 9 templates estão em [`.claude/docs/WHATSAPP_TEMPLATES.md`](.claude/docs/WHATSAPP_TEMPLATES.md).

- Cada aviso é gravado na outbox (`NotificationOutbox`) na mesma transação da mudança e enviado por um worker dentro do app a cada ~10 s. Falhas passageiras são repetidas (1 min, 5 min, 30 min, 2 h); na 5ª falha, ou numa falha definitiva, o aviso fica `Failed` e é auditado.
- Avisos entre 22:00 e 08:00 (Brasília) saem às 08:00; códigos saem na hora.
- Aluno sem celular confirmado (ou sem conta) não recebe aviso: a linha fica `Failed` com `sem-celular`.
- **Esqueci minha senha** (`/esqueci-senha` → `/nova-senha`): código no celular confirmado da conta (no personal, o `--phone` do provisionamento). A resposta é a mesma exista ou não a conta (D-058).
- Configuração do worker: `Notifications:Worker:Enabled`, `PollInterval` (padrão `00:00:10`), `BatchSize` (20) e `Lease` (`00:02:00`).

### Configurar WhatsApp

**Desenvolvimento (padrão):** `WhatsApp:Provider = Log`. Nada é enviado; o log mostra o template, os parâmetros (inclusive o código) e o link do botão, com o celular mascarado:

```text
WhatsApp (provider Log) ph_codigo para +55•••••••2468: [482913] botão  (log.…)
```

O código dos testes manuais (convite, senha, confirmar celular) sai desse log. Em Development, sem `PhoneVerification:HashKey`, cada execução sorteia uma chave: códigos pedidos antes de reiniciar o app deixam de valer.

**Meta (WhatsApp Cloud API):**

1. Na Meta for Developers, crie (ou use) o app com o produto **WhatsApp** e anote o **Phone number ID** e o **App secret** (Configurações do app → Básico). Gere um **token de acesso permanente** de um usuário do sistema com as permissões `whatsapp_business_messaging` e `whatsapp_business_management`.
2. Cadastre e aguarde a **aprovação dos 9 templates** de `WHATSAPP_TEMPLATES.md` (idioma Português (BR)). Antes disso, os envios falham com `meta:132001`.
3. Configure os segredos (nunca no repositório):

```bash
dotnet user-secrets set "WhatsApp:Provider" "Meta" --project src/PersonalHub.Web
```

```bash
dotnet user-secrets set "WhatsApp:PhoneNumberId" "SEU_PHONE_NUMBER_ID" --project src/PersonalHub.Web
```

```bash
dotnet user-secrets set "WhatsApp:AccessToken" "SEU_TOKEN" --project src/PersonalHub.Web
```

```bash
dotnet user-secrets set "WhatsApp:AppSecret" "SEU_APP_SECRET" --project src/PersonalHub.Web
```

```bash
dotnet user-secrets set "WhatsApp:WebhookVerifyToken" "UM_TEXTO_ALEATORIO" --project src/PersonalHub.Web
```

```bash
dotnet user-secrets set "PhoneVerification:HashKey" "$(openssl rand -base64 32)" --project src/PersonalHub.Web
```

   Fora de Development, use as variáveis de ambiente equivalentes (`WhatsApp__AccessToken`, `PhoneVerification__HashKey` etc.). `WhatsApp:ApiVersion` tem padrão `v23.0`. Com `Provider = Meta`, o app não sobe sem `PhoneNumberId` e `AccessToken`; em Production, `Provider = Log` não é aceito e `PhoneVerification:HashKey` é obrigatória fora de Development. Trocar a `HashKey` invalida só os códigos pendentes.
4. **Webhook** (entregue/lido/falhou): em WhatsApp → Configuração, informe a URL `https://<App:PublicBaseUrl>/webhooks/whatsapp`, o mesmo `WebhookVerifyToken` e assine o campo **messages**. A Meta exige HTTPS público (em desenvolvimento, um túnel como `ngrok`/`cloudflared`). Sem `WhatsApp:AppSecret`, o endpoint responde 404.
5. **Número de teste da Meta:** envia só para até 5 números cadastrados como destinatários de teste no painel, e só templates aprovados. Use-o enquanto a conta empresarial não estiver verificada/liberada; para enviar a qualquer aluno, é preciso um número próprio registrado e a conta liberada (verificação da empresa e limites de envio da Meta).

## Autorização

Toda rota exige login por padrão (`FallbackPolicy`). Uma página nova sem atributo fica protegida; as públicas precisam de `[AllowAnonymous]` explícito: `/`, `/entrar`, `/convite/{token}`, `/esqueci-senha`, `/nova-senha`, `/webhooks/whatsapp`, `/termos`, `/privacidade`, `/Error`, `/not-found`, `/health`, `POST /sair` e os arquivos estáticos. O teste `AuthorizationPolicyTests` falha se uma rota pública nova aparecer fora dessa lista.

As páginas do personal usam a policy `ActivePersonal`: role `Personal` **e** perfil ativo no banco, conferido a cada requisição. Um personal desativado com o cookie ainda válido tem a sessão encerrada e volta para `/entrar`.

## Configuração de produção

Fora de Development, estes valores são obrigatórios (variáveis de ambiente ou o serviço de segredos da hospedagem; nada disso vai para o repositório):

| Chave | Variável de ambiente | Exemplo | Para quê |
| --- | --- | --- | --- |
| `App:PublicBaseUrl` | `App__PublicBaseUrl` | `https://app.exemplo.com` | Endereço público usado nos links enviados para fora, como o do convite. URL `https` absoluta, sem caminho. |
| `AllowedHosts` | `AllowedHosts` | `app.exemplo.com` | Hosts aceitos pela aplicação (separados por `;`). |
| `PhoneVerification:HashKey` | `PhoneVerification__HashKey` | (32 bytes em base64) | Chave do HMAC dos códigos do WhatsApp. |
| `WhatsApp:*` | `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`, … | | Provider `Meta` e credenciais (seção "Configurar WhatsApp"). `appsettings.Production.json` já define `Provider = Meta`. |

- Sem `App:PublicBaseUrl` válida, a aplicação **não sobe** (erro na inicialização citando a chave). O link do convite nunca usa o header `Host` da requisição, que o cliente controla.
- `appsettings.Production.json` traz `AllowedHosts` com o placeholder `configure-o-dominio.invalid`. Se ele não for trocado, toda requisição recebe 400.
- Em Development, `App:PublicBaseUrl` é opcional. Sem ela, os links usam o endereço da requisição.

## Observabilidade

**Logs.** A aplicação escreve logs no console, com o formato definido pelo ambiente:
- **Development:** uma linha legível por evento, com os escopos ativos. O SQL gerado pelo EF Core aparece em nível Information, sem os valores dos parâmetros.
- **Demais ambientes (Production etc.):** JSON, uma linha por evento, com timestamp em UTC e os escopos. É o formato pronto para um coletor de logs.

Os níveis por categoria ficam em `appsettings.json`, `appsettings.Development.json` e `appsettings.Production.json`. Para ver os logs de Production localmente:

```bash
ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__PersonalHub="Host=localhost;..." dotnet run --project src/PersonalHub.Web --no-launch-profile
```

Cada requisição HTTP gera uma linha com método, caminho, status e duração. Ficam de fora `/health` e os arquivos estáticos. A query string, os corpos e os headers nunca são registrados, e o token do convite sai como `/convite/[redacted]`. A categoria `Microsoft.AspNetCore.Hosting.Diagnostics` fica desligada em código: o escopo de log dela (`RequestPath`) copiaria o caminho bruto, com o token, para todas as linhas da requisição.

**Correlation ID.** Toda resposta traz o header `X-Correlation-ID`:
- Se a requisição já trouxer um valor válido (até 64 caracteres, só letras, dígitos e hífen), ele é mantido.
- Caso contrário, usa-se o TraceId da requisição.

O mesmo id aparece no escopo `CorrelationId` de todos os logs da requisição e na página de erro. Assim, um usuário pode informar o código e você encontra os logs correspondentes. Em Blazor Server, os eventos de um circuito carregam o id da requisição que abriu a conexão.

**Nunca registrar em logs:** senha, tokens (de convite, sessão, API ou WhatsApp), CPF, conteúdo de mensagens WhatsApp, connection strings, nem dados pessoais além do necessário. Nunca habilitar `EnableSensitiveDataLogging` do EF Core.

**Erros.** Fora de Development, uma exceção não tratada mostra só uma mensagem genérica e o correlation ID, sem stack trace. Os detalhes vão para o log. `/health` responde apenas `Healthy` ou `Unhealthy`.

## Testes

Os testes de integração usam o PostgreSQL do Docker Compose com um banco separado, criado e apagado a cada execução. O nome do banco precisa conter `test` (proteção contra apagar o banco de desenvolvimento).

Configure uma vez (mesmo usuário e senha do `.env`):

```bash
dotnet user-secrets set "ConnectionStrings:PersonalHubTests" "Host=localhost;Port=5432;Database=personalhub_tests;Username=personalhub;Password=SUA_SENHA" --project tests/PersonalHub.IntegrationTests
```

Ou use a variável de ambiente `ConnectionStrings__PersonalHubTests`. Depois, com o container rodando:

```bash
dotnet test
```

## Estrutura

```text
src/PersonalHub.Web/          Aplicação Blazor
  Domain/                     Entidades e regras de domínio
  Features/                   Fatias verticais por caso de uso
  Infrastructure/             Persistência, identidade e integrações
tests/PersonalHub.UnitTests/         Testes de domínio
tests/PersonalHub.IntegrationTests/  Testes de integração
```
