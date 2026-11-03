# PersonalHub — Visão do produto

## 1. Objetivo

O PersonalHub é uma aplicação web mobile first que funciona como central de controle da relação entre personal trainers e alunos. Permite registrar aulas, solicitar reconhecimento do aluno, acompanhar histórico e organizar o fechamento mensal e os pagamentos.

A aplicação deve oferecer transparência: personal e aluno consultam as mesmas informações relevantes sobre aulas e períodos, respeitando suas permissões.

## 2. Perfis de usuário

### Personal trainer
- Acessa a aplicação após autenticação.
- Cadastra e gerencia seus alunos.
- Define informações comerciais necessárias para cobrança, incluindo valor da aula e vencimento mensal (detalhes a confirmar).
- Registra aulas para seus próprios alunos.
- Consulta aulas pendentes, aprovadas/reconhecidas e recusadas.
- Exclui aulas; se a aula já tiver sido reconhecida ou recusada, informa o motivo.
- Recebe notificação quando o aluno reconhece ou recusa uma aula.
- Consulta faturamento e situação dos pagamentos.
- Confirma o recebimento de um pagamento informado pelo aluno.

### Aluno
- Acessa a aplicação após autenticação.
- Consulta seu histórico de aulas e seus estados.
- Aprova/reconhece ou recusa uma aula registrada pelo personal.
- Pode informar pelo fluxo da aplicação que realizou o pagamento do período.
- Consulta demonstrativo mensal e valor devido.
- Recebe notificações de novas aulas, exclusões relevantes e cobrança mensal.

### Administrador operacional (MVP)
- Os personais serão cadastrados manualmente por administradores diretamente no banco de dados para o MVP.
- Não é necessário fluxo público de cadastro de personal.
- **A DEFINIR:** se haverá uma interface administrativa mínima ou se a operação será exclusivamente via procedimento controlado de seed/script/SQL. Não criar uma tela administrativa sem aprovação.

## 3. Fluxos principais

### 3.1 Cadastro de aluno
1. Personal informa dados pessoais do aluno e dados necessários à cobrança.
2. Sistema valida os dados e associa o aluno ao personal autenticado.
3. Aluno recebe acesso/convite de forma segura.
4. O aluno passa a visualizar somente suas próprias informações.

**A DEFINIR:** campos pessoais obrigatórios, CPF obrigatório ou opcional, fluxo de convite, possibilidade de aluno associado a mais de um personal e política para e-mail/telefone duplicado.

### 3.2 Registro e reconhecimento de aula
1. Personal seleciona um aluno e registra uma aula.
2. Sistema cria aula com estado inicial pendente.
3. Sistema solicita reconhecimento ao aluno pela aplicação e/ou WhatsApp.
4. Aluno reconhece/aprova ou recusa.
5. Sistema registra decisão, data/hora e, se houver, justificativa.
6. Personal recebe notificação WhatsApp da decisão.
7. Ambos consultam o histórico e o estado atualizado.

Somente o personal pode criar/registrar aulas. O aluno não pode criar ou editar registros de aula; pode apenas reconhecer/aprovar ou recusar.

**A DEFINIR:** se a recusa exige justificativa; prazo para resposta; lembretes; como corrigir uma aula recusada; se o reconhecimento por WhatsApp ocorrerá por link seguro ou resposta interativa; como impedir decisões repetidas.

### 3.3 Exclusão de aula
- Personal pode excluir aula pendente, reconhecida ou recusada.
- Se a aula estiver pendente, registrar a exclusão e atualizar o estado/visibilidade conforme política definida.
- Se estiver reconhecida ou recusada, o personal deve informar motivo obrigatório.
- Ao excluir uma aula já reconhecida ou recusada, enviar WhatsApp ao aluno explicando a exclusão e o motivo.
- Preservar trilha de auditoria; evitar exclusão física que apague evidências.

**A DEFINIR:** exclusão lógica (recomendada) e estados específicos de cancelamento/exclusão; se a exclusão de aula pendente também notifica o aluno.

### 3.4 Fechamento mensal e cobrança
1. Na data de vencimento configurada para o aluno, sistema prepara o demonstrativo do período.
2. Demonstrativo apresenta aulas contabilizadas, valor unitário aplicável, total e período de referência.
3. Sistema envia notificação WhatsApp ao aluno.
4. Aluno consulta demonstrativo e informa que realizou o pagamento.
5. Sistema registra pagamento como informado pelo aluno, ainda não confirmado pelo personal.
6. Personal recebe notificação WhatsApp.
7. Personal confirma recebimento.
8. Período passa a constar como pago.

**A DEFINIR:** regra exata de período (mês calendário anterior ou período entre vencimentos), valor por aula versus mensalidade fixa, descontos/acréscimos, vencimento em dia inexistente (ex.: dia 31), fuso horário, pagamentos parciais e tratamento de ajustes após fechamento.

### 3.5 Notificações
Eventos previstos:
- Nova aula registrada → aluno.
- Aula reconhecida/aprovada → personal que registrou.
- Aula recusada → personal que registrou.
- Aula já reconhecida/recusada excluída → aluno, com motivo.
- Data de vencimento e demonstrativo → aluno.
- Pagamento informado pelo aluno → personal.
- **A DEFINIR:** confirmação de recebimento pelo personal deve ou não gerar notificação ao aluno.

O WhatsApp é o canal explicitamente solicitado. A aplicação também deve exibir estados e notificações internas quando aplicável, mas escopo exato de central de notificações deve ser aprovado.

## 4. Estados de aula

Proposta inicial:
- `Pending`: registrada, aguardando reconhecimento do aluno.
- `Approved`: reconhecida/aprovada pelo aluno.
- `Rejected`: recusada pelo aluno.
- `Deleted` ou estado de cancelamento lógico: removida pelo personal, mantendo auditoria.

Uma aula excluída não deve desaparecer da trilha de auditoria. Definir se o histórico do aluno exibe aulas excluídas e como são apresentadas.

Transições propostas:
- Criação pelo personal → `Pending`.
- Aluno → `Approved` ou `Rejected`.
- Personal → exclusão lógica a partir de qualquer estado permitido.
- Não permitir que aluno altere uma aula já decidida, salvo regra futura aprovada.

## 5. Escopo do MVP

### Dentro do MVP
- Autenticação e autorização para personal e aluno.
- Provisionamento manual de personais.
- Cadastro e gestão básica de alunos.
- Associação segura entre personal e aluno.
- Registro de aulas pelo personal.
- Reconhecimento/aprovação e recusa pelo aluno.
- Histórico e filtros por período/estado.
- Exclusão lógica de aula com motivo obrigatório quando já decidida.
- Cálculo e visualização de demonstrativo mensal.
- Fluxo de pagamento informado pelo aluno e confirmado pelo personal.
- Notificações WhatsApp dos eventos descritos, após seleção do provedor.
- Interface responsiva, priorizando celular.

### Fora do MVP
- Planos de assinatura para personal.
- Cobrança recorrente do personal e integração com gateway de pagamento.
- Marketplace ou busca pública de personal.
- Cadastro público de personal.
- Aplicativos nativos iOS/Android.
- Funcionalidades avançadas de BI.
- Integrações não aprovadas.

## 6. Critérios gerais de sucesso
- Personal consegue registrar uma aula em poucos passos pelo celular.
- Aluno consegue reconhecer ou recusar uma aula com clareza.
- Personal e aluno visualizam estados consistentes.
- Demonstrativo mensal é rastreável e explicável.
- Pagamento só é considerado confirmado após ação do personal.
- Um usuário nunca acessa dados de outro personal/aluno sem autorização.
- Falhas de WhatsApp não causam perda do registro de aula ou pagamento.