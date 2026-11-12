# PersonalHub — Arquitetura

## 1. Diretriz

Adotar Vertical Slice Architecture (VSA), organizada por feature/caso de uso. Cada fatia concentra o que precisa para executar uma capacidade do sistema: endpoint/página, validação, handler, DTO e acesso a dados específico, mantendo regras de domínio reutilizáveis quando houver justificativa.

Evitar arquitetura em camadas horizontais extensas como `Controllers/Services/Repositories` para todo o sistema. A organização deve facilitar localizar e alterar uma funcionalidade sem navegar por dezenas de pastas globais.

## 2. Plataforma web

Usar .NET 10 com Blazor Web App configurado para Interactive Server, que atende à experiência solicitada como Blazor Server. Confirmar o template e o modo de renderização ao criar a solução.

Componentes Razor devem cuidar de apresentação e interação. Regras de autorização, validação e negócio precisam ser executadas no servidor, fora da confiança do browser.

## 3. Estrutura sugerida

Ajustar à solução existente após inspeção; não recriar projeto sem necessidade.

```text
src/
  PersonalHub.Web/
    Components/
      Layout/
      Shared/
    Features/
      Authentication/
        Login/
        Logout/
      Dashboard/
        PersonalHome/
      Students/
        CreateStudent/
        ListStudents/
        StudentDetails/
      Lessons/
        RegisterLesson/
        ListLessons/
        RespondToLesson/
        DeleteLesson/
      Billing/
        ViewStatement/
        ReportPayment/
        ConfirmPayment/
      Notifications/
    Infrastructure/
      Persistence/
      Identity/
      WhatsApp/           (adapter Meta/Log, opções, webhook)
      BackgroundJobs/
    wwwroot/
      css/
      js/
      images/
tests/
  PersonalHub.UnitTests/
  PersonalHub.IntegrationTests/
```

Se houver necessidade real de separar domínio e infraestrutura em projetos, propor uma estrutura enxuta, por exemplo:
- `PersonalHub.Domain`
- `PersonalHub.Infrastructure`
- `PersonalHub.Web`
- projetos de testes.

Não criar projeto extra apenas por convenção. A estrutura final deve continuar feature based.

## 4. Limites de responsabilidade

### Features
- Orquestram um caso de uso.
- Validam dados de entrada.
- Aplicam autorização.
- Invocam regras de domínio.
- Persistem alterações.
- Produzem DTOs/view models para a interface.

### Domain
- Regras e invariantes de negócio.
- Estados e transições válidas.
- Cálculos de cobrança.
- Não depende de Blazor, EF Core, WhatsApp ou AWS.

### Infrastructure
- Implementação de persistência PostgreSQL/EF Core.
- ASP.NET Core Identity.
- Envio WhatsApp por adapter.
- Worker de notificações e processamento agendado.
- Implementações de relógio, serviços externos e observabilidade.

## 5. Persistência

- PostgreSQL via Docker Compose em desenvolvimento.
- EF Core migrations versionadas.
- `DbContext` com configuração explícita de relacionamentos, índices, constraints e precisão monetária.
- Transações em operações que gravem dados de domínio e outbox.
- Não executar migrations automaticamente em produção sem estratégia de deploy aprovada.
- Dados de desenvolvimento devem ser fictícios.

## 6. WhatsApp e processamento assíncrono

Definir interface de aplicação para solicitar envio de notificação. O caso de uso grava uma mensagem na outbox dentro da mesma transação da alteração de negócio. Um worker processa a outbox, envia ao provedor e atualiza tentativas/estado.

O adapter deve:
- suportar timeout e cancelamento;
- tratar falhas transitórias e permanentes;
- aplicar backoff e limite de tentativas;
- evitar duplicidade quando o provedor suportar idempotência;
- registrar identificador de mensagem;
- validar autenticidade de webhooks;
- não registrar payload sensível em logs.

**Implementação (Fase 6, D-055 a D-062):**

```text
Handler (transação) ──► NotificationOutbox (Pending, IdempotencyKey única)
                                  │
NotificationWorker (a cada ~10 s) │  ClaimAsync: FOR UPDATE SKIP LOCKED → Processing + lease (2 min), Attempts+1
                                  ▼
                      NotificationDispatcher ──► IWhatsAppSender ──► Meta Cloud API / log
                                  │
                 Sent (ProviderMessageId) │ Pending + NextAttemptAtUtc (1 min, 5 min, 30 min, 2 h) │ Failed (auditado)
                                  ▲
       /webhooks/whatsapp (assinado) ── entregue / lido / falhou, por ProviderMessageId
```

- `Features/Notifications`: `NotificationOutboxWriter` (enfileira no mesmo `DbContext`, checa a chave antes do índice único), `BusinessNotifications` (um método por evento de negócio, chamado pelos handlers), `NotificationContacts` (quem recebe e em qual celular), `NotificationDispatcher` e `NotificationWorker`.
- `Infrastructure/WhatsApp`: `IWhatsAppSender`; `MetaCloudWhatsAppSender` (HttpClient tipado, `POST /{ApiVersion}/{PhoneNumberId}/messages`, Bearer, template com parâmetros do corpo e do botão; erros 429/5xx/131000 e limites repetem, 131026/131031/132xxx/100 não); `LogWhatsAppSender` (padrão em Development; telefone mascarado); `WhatsAppOptions` validadas na partida; `WhatsAppWebhook`.
- `Features/PhoneVerification`: `PhoneCodeService` (envio e conferência dos códigos), `ConfirmPhone` (alunos de antes da Fase 6 e o middleware `StudentPhoneGate`). `Features/PasswordReset`: `/esqueci-senha` e `/nova-senha`.
- Escolha do adapter: `WhatsApp:Provider` = `Log` ou `Meta`. Testes trocam o adapter por um falso e chamam o dispatcher direto (`Notifications:Worker:Enabled = false`).
- Horário de silêncio (22h–8h de São Paulo) aplicado ao agendar e ao reagendar; códigos (`IsAuthentication`) saem na hora e expiram com o código (`ExpiresAtUtc`).
- Um processo que cai no meio do envio deixa a linha em `Processing`; quando o lease vence, outra rodada a retoma. A tentativa é contada ao pegar a linha. Resultado gravado com `xmin`: se outra instância retomou a linha, o resultado atrasado é descartado.
- Possível duplicata residual: a Meta não aceita chave de idempotência; se o processo cair depois de a Meta aceitar e antes de gravar `Sent`, a retomada envia de novo. Aceito no MVP (janela de segundos).
- Métricas: por enquanto, logs estruturados por rodada (reivindicados, enviados, reagendados, falhos) e `LastError` sanitizado na outbox.

## 7. Agendamento de vencimentos

O envio de demonstrativos na data de vencimento exige processamento agendado. A solução escolhida deve ser simples no MVP, confiável e compatível com hospedagem futura na AWS.

O agente deve comparar alternativas (por exemplo, BackgroundService persistente versus serviço gerenciado de agendamento) e apresentar impactos de disponibilidade, escalabilidade, custo e operação. Não usar timer em memória como mecanismo único para tarefas que não podem ser perdidas.

O processamento deve ser idempotente: reiniciar a aplicação ou executar o job mais de uma vez não pode emitir cobranças duplicadas.

**Escolha (D-047, Fase 4):** `BackgroundService` no próprio app (`Features/Billing/CloseBilling/BillingClosingWorker`), que roda na partida e a cada hora (`Billing:Closing:Interval`; `Billing:Closing:Enabled` desliga, como nos testes).

- O job não depende de "o que mudou desde a última vez": a cada execução olha o banco e fecha o mês mais recente já vencido de cada vínculo que ainda não tem demonstrativo. Um dia perdido (app fora do ar, deploy) é recuperado na execução seguinte; não é um timer em memória como mecanismo único.
- O resultado não depende da hora em que roda: o corte é 00:00 de São Paulo no dia do vencimento (D-043).
- Concorrência: `pg_try_advisory_lock` de sessão garante uma execução por vez entre instâncias; quem não pega o lock pula a rodada. Cada vínculo fecha numa transação própria com `SELECT ... FOR UPDATE` no vínculo; os índices únicos `(PersonalStudentId, ReferenceMonth)` e `(LessonId, Kind)` são a última barreira.
- Falha num vínculo é logada e não interrompe os outros. Logs com ids e contagens, sem nomes ou valores de aula.
- Alternativa descartada no MVP: agendador gerenciado (EventBridge Scheduler chamando um endpoint protegido). Fica como opção se o app passar a escalar a zero ou se o fechamento precisar sair do processo web.
- Execução manual: `dotnet run --project src/PersonalHub.Web -- close-billing [--date aaaa-mm-dd]` (`--date` só em Development).

## 8. Docker local

Criar `docker-compose.yml` para PostgreSQL com:
- imagem versionada, não `latest`;
- volume persistente;
- healthcheck;
- usuário e senha de desenvolvimento via `.env` não versionado ou User Secrets;
- porta configurável;
- instruções de inicialização e reset seguro.

Não incluir dados reais nem segredos no repositório.

## 9. Bootstrap e jQuery

- Bootstrap é a base de grid, breakpoints, formulários, botões, navegação, modais e utilitários.
- PersonalHub deve possuir tokens de tema próprios em CSS, sem sobrescrever indiscriminadamente classes do Bootstrap.
- jQuery só deve ser incluído quando um plugin ou integração específica exigir. Para interações novas, preferir recursos nativos do Blazor/JavaScript interoperável.
- Evitar manipulação do mesmo DOM simultaneamente pelo Blazor e jQuery.
- Usar JavaScript interop com ciclo de vida e descarte corretos.

## 10. Observabilidade mínima

- Logs estruturados.
- Correlation ID/trace context.
- Logs de falha de autenticação, autorização, processamento de outbox e jobs sem expor dados pessoais.
- Métricas básicas de erros, latência e fila de notificações.
- Health checks para aplicação e banco.
- Não registrar conteúdo integral de mensagens WhatsApp, CPF, senha ou tokens.

## 11. AWS

Serviços AWS ainda não definidos. Não acoplar o código a serviços AWS antes de uma decisão arquitetural. Preparar configuração por ambiente e adapters. Quando chegar o momento, comparar opções de hospedagem, banco gerenciado, segredos, arquivos, observabilidade, rede, backup e recuperação.