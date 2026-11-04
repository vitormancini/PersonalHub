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
