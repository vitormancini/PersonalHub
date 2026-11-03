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

## Executar a aplicação

```bash
dotnet run --project src/PersonalHub.Web
```

A URL aparece no terminal. O health check da aplicação fica em `/health`.

## Testes

```bash
dotnet test
```

## Estrutura

```text
src/PersonalHub.Web/          Aplicação Blazor
  Features/                   Fatias verticais por caso de uso
  Infrastructure/             Persistência, identidade e integrações
tests/PersonalHub.UnitTests/         Testes de domínio
tests/PersonalHub.IntegrationTests/  Testes de integração
```
