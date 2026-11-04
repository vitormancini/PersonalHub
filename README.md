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
- O aluno abre o link, cria a senha (ou entra com a senha atual, se o e-mail já tiver conta), aceita os termos e cai em `/aluno`.
- O caminho `/convite/*` é limitado a 20 requisições por minuto por IP (`Students:InviteRateLimit`), responde com `Referrer-Policy: no-referrer` e aparece nos logs como `/convite/[redacted]`.

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
