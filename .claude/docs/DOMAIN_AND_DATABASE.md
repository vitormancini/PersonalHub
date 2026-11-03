# PersonalHub — Domínio e modelagem inicial do banco

## 1. Objetivo

Este documento apresenta uma proposta inicial de entidades e relações para discussão. Não executar migrations nem cristalizar o modelo antes de validar as regras marcadas como **A DEFINIR**.

Banco: PostgreSQL. ORM sugerido: Entity Framework Core com provider Npgsql compatível com .NET 10.

## 2. Princípios de modelagem
- Usar chaves primárias UUID (`uuid`) para entidades de negócio expostas por APIs/rotas.
- Datas armazenadas em UTC (`timestamp with time zone` no PostgreSQL) e convertidas para o fuso definido pelo produto na apresentação e no cálculo de vencimento.
- Valores monetários com `numeric(12,2)` e moeda BRL explicitada no domínio; nunca usar `float`/`double` para dinheiro.
- Definir precisão e escala após confirmar limites comerciais.
- Usar exclusão lógica/auditoria para registros com relevância histórica.
- Criar índices para consultas por proprietário, aluno, período e estado.
- Aplicar restrições de integridade no banco sempre que possível.
- Evitar duplicar dados pessoais sem necessidade.
- Não armazenar dados de cartão ou credenciais de pagamento no MVP.

## 3. Entidades propostas

### 3.1 User / Identity
Usar ASP.NET Core Identity (ou alternativa aprovada) para identidade e autenticação.
Campos conceituais:
- Id
- UserName / Email normalizado
- PasswordHash gerenciado pelo Identity
- PhoneNumber
- PhoneNumberConfirmed
- Lockout e campos de segurança do Identity

Não criar tabela de senha própria. Perfis e autorização devem distinguir `Personal` e `Student` por roles/claims e vínculos de domínio.

### 3.2 PersonalProfile
- Id
- UserId (FK para usuário)
- DisplayName
- CreatedAtUtc
- IsActive

Personal é provisionado manualmente no MVP por processo operacional controlado. Não permitir autoelevação para role Personal.

### 3.3 StudentProfile
- Id
- UserId (FK; pode ser nulo antes do convite, se esse fluxo for aprovado)
- FullName
- CPF (opcional/definir obrigatoriedade; se armazenado, proteger e restringir acesso)
- PhoneNumber
- Email
- CreatedAtUtc
- IsActive

**A DEFINIR:** cadastro cria imediatamente uma conta Identity ou cria aluno pendente de convite? O personal pode cadastrar aluno sem e-mail? Como tratar CPF duplicado e aluno que troca de personal?

### 3.4 PersonalStudent
Vínculo explícito entre personal e aluno:
- Id
- PersonalId
- StudentId
- LinkedAtUtc
- Status (Active/Inactive)
- BillingDueDay
- LessonPrice (se preço por aula for confirmado)
- CurrencyCode (BRL)

Esse vínculo é o local natural para configurações comerciais específicas daquela relação.

**A DEFINIR:** um aluno pode estar ligado a mais de um personal? Se sim, garantir unicidade por par e separar configurações por vínculo. Se não, impor regra de vínculo único ativo.

### 3.5 Lesson
- Id
- PersonalStudentId
- CreatedByPersonalUserId
- OccurredAtUtc (data/hora em que a aula aconteceu)
- DurationMinutes (opcional)
- Notes (opcional; avaliar dados pessoais/sensíveis)
- Status (Pending, Approved, Rejected, Deleted)
- CreatedAtUtc
- RespondedAtUtc (nullable)
- RejectionReason (nullable, se justificativa for aprovada)
- DeletedAtUtc (nullable)
- DeletedByUserId (nullable)
- DeletionReason (obrigatória se excluir aula já decidida)
- RowVersion/concurrency token ou mecanismo equivalente

Separar `OccurredAtUtc` de `CreatedAtUtc`: a aula pode ser registrada depois de ter ocorrido, se a regra permitir.

**A DEFINIR:** data retroativa permitida? Duração necessária? Uma aula pode ser editada? Qual regra de duplicidade? A recusa exige justificativa?

### 3.6 BillingPeriod
Representa o fechamento de um período de cobrança de um aluno.
- Id
- PersonalStudentId
- PeriodStart
- PeriodEnd (definir inclusividade)
- DueDate
- Status (Open, Closed/Issued, PaymentReported, Paid, Adjusted/Cancelled — validar estados)
- CurrencyCode
- TotalAmount
- CreatedAtUtc
- ClosedAtUtc (nullable)
- PaidAtUtc (nullable)

Criar restrição única para impedir dois fechamentos do mesmo vínculo e período.

O período deve ser imutável após fechamento, ou possuir mecanismo explícito de ajuste/versionamento. Não recalcular silenciosamente um demonstrativo já enviado.

### 3.7 BillingPeriodLesson
Snapshot das aulas que compõem o fechamento:
- Id
- BillingPeriodId
- LessonId
- UnitPrice
- Amount
- LessonDescriptionSnapshot ou campos mínimos para demonstrativo

A tabela snapshot preserva exatamente quais aulas e valores foram cobrados, mesmo que configurações de preço mudem depois.

### 3.8 Payment
Registro de comunicação e confirmação de pagamento:
- Id
- BillingPeriodId
- ReportedByStudentUserId
- ReportedAtUtc
- ConfirmedByPersonalUserId (nullable)
- ConfirmedAtUtc (nullable)
- Amount
- Status (Reported, Confirmed, Disputed/Cancelled — definir)
- Notes (opcional; não solicitar comprovante no MVP sem aprovação)

Não considerar o período pago apenas porque o aluno informou o pagamento. O estado `Paid` só ocorre após confirmação do personal.

### 3.9 NotificationOutbox
Fila persistente de notificações a enviar:
- Id
- EventType
- RecipientUserId / Phone snapshot conforme necessidade
- Payload (JSON validado e sem segredos)
- Channel (WhatsApp)
- Status (Pending, Processing, Sent, Failed)
- Attempts
- NextAttemptAtUtc
- CreatedAtUtc
- SentAtUtc
- ProviderMessageId
- LastError (sanitizado)

A outbox permite salvar a operação de negócio e a intenção de notificar na mesma transação. Um worker envia posteriormente e aplica retry controlado. Nunca depender do envio síncrono de WhatsApp para concluir registro de aula ou pagamento.

### 3.10 AuditLog
Trilha de auditoria para ações críticas:
- Id
- ActorUserId
- Action
- EntityType
- EntityId
- OccurredAtUtc
- CorrelationId
- Metadata minimizada e sanitizada

Não copiar CPF, senha, tokens ou conteúdo pessoal desnecessário para auditoria.

## 4. Relações conceituais

- User 1:1 PersonalProfile (para usuários personal).
- User 1:1 StudentProfile (para usuários aluno).
- PersonalProfile 1:N PersonalStudent.
- StudentProfile 1:N PersonalStudent.
- PersonalStudent 1:N Lesson.
- PersonalStudent 1:N BillingPeriod.
- BillingPeriod 1:N BillingPeriodLesson.
- Lesson 1:N BillingPeriodLesson, conforme política de fechamento.
- BillingPeriod 1:N Payment (ou 1:1 se pagamento único for decidido).
- Eventos de negócio geram 1:N NotificationOutbox.
- Ações relevantes geram AuditLog.

## 5. Restrições e índices sugeridos

Validar antes de criar:
- Índices em `PersonalStudent(PersonalId, Status)` e `PersonalStudent(StudentId, Status)`.
- Índices em `Lesson(PersonalStudentId, OccurredAtUtc)` e `Lesson(PersonalStudentId, Status)`.
- Índice em `BillingPeriod(PersonalStudentId, PeriodStart, PeriodEnd)`.
- Índice em `NotificationOutbox(Status, NextAttemptAtUtc)`.
- Restrição única para evitar fechamento duplicado de período.
- FK obrigatória em todos os vínculos.
- Check constraints para valores monetários não negativos e dias de vencimento válidos (1 a 31).
- Concorrência otimista para evitar duas respostas conflitantes para a mesma aula.
- Idempotência para callbacks/webhooks e comandos que possam ser repetidos.

## 6. Regras de consistência importantes
- Somente o personal proprietário do vínculo pode registrar ou excluir aulas.
- Somente o aluno associado pode aprovar/recusar uma aula.
- A resposta só pode ser aplicada se a aula estiver pendente.
- O personal não pode confirmar pagamento de período pertencente a outro personal.
- O aluno só pode reportar pagamento do próprio período.
- Aula incluída em fechamento deve ser identificável e não cobrada em dois períodos.
- Exclusão de aula após fechamento deve gerar política de ajuste explícita; nunca alterar total histórico silenciosamente.
- Notificação pode falhar sem desfazer a operação de domínio; a outbox deve permitir retentativa.
- Todas as operações críticas devem ser seguras contra repetição.

## 7. Decisões de negócio que precisam ser aprovadas antes do modelo final

1. Um aluno pode ter mais de um personal?
2. O aluno terá login próprio desde o cadastro ou receberá convite?
3. Quais dados pessoais são realmente necessários? CPF é obrigatório?
4. O preço é por aula, mensalidade fixa ou ambos?
5. O preço pode variar ao longo do tempo? Como preservar preço histórico?
6. Qual é a definição exata do período mensal: mês calendário ou ciclo de vencimento?
7. A aula é cobrada quando está pendente, apenas quando aprovada, ou após outra regra?
8. Como tratar aula recusada?
9. Qual prazo o aluno tem para reconhecer/recusar?
10. A exclusão de aula já incluída em período fechado gera reabertura, crédito ou ajuste no próximo período?
11. Pagamento parcial é permitido?
12. Um período pode ter mais de um pagamento?
13. O que acontece se o dia de vencimento for 29, 30 ou 31 em um mês curto?
14. Qual fuso horário será usado? Proposta: `America/Sao_Paulo`, confirmando se o produto é inicialmente Brasil-only.
15. Como deve ser tratado o encerramento do vínculo entre personal e aluno?

## 8. Entregável de modelagem

Antes da implementação, o agente deve apresentar:
- Diagrama ER (Mermaid).
- Descrição de tabelas, colunas, tipos, PKs, FKs, índices e constraints.
- Estados e transições de aula, cobrança e pagamento.
- Estratégia de Identity e vínculo entre usuário e perfil.
- Exemplos de consultas principais.
- Justificativa para snapshot de cobrança e outbox.
- Dúvidas pendentes e proposta de resolução.

Aguardar aprovação explícita antes de criar migrations.