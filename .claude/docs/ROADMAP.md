# PersonalHub — Roadmap do MVP

A sequência é uma proposta. O agente deve validar dependências e obter aprovação antes de iniciar cada etapa. Cada feature segue obrigatoriamente design → aprovação → implementação → testes.

## Fase 0 — Descoberta e fundação
- [x] Inspecionar repositório existente.
- [x] Confirmar versão do SDK e estrutura da solução.
- [x] Resolver decisões de domínio pendentes que bloqueiam o banco (D-015 a D-029, 2026-11-03).
- [x] Apresentar diagrama ER e estados.
- [x] Aprovar arquitetura e modelo de dados (2026-11-03).
- [x] Configurar solução, PostgreSQL Docker e migrations iniciais após aprovação.
- [x] Configurar logs estruturados, health checks e ambientes.

## Fase 1 — Identidade e acesso
- [x] Login/logout.
- [x] Roles Personal e Student.
- [x] Provisionamento manual de personal.
- [x] Fluxo seguro de criação/convite de aluno (2026-11-04).
- [x] Autorização e isolamento de dados (2026-11-04).
  Critério: usuários autenticados acessam apenas funcionalidades e dados do próprio perfil/vínculo.

## Fase 2 — Alunos
- [x] Listar alunos (2026-11-05): busca por nome e filtros por situação.
- [x] Cadastrar aluno (2026-11-05): cadastro da Fase 1 + reenvio e cancelamento do convite (D-034).
- [x] Consultar detalhes (2026-11-05).
- [x] Editar dados permitidos (2026-11-05): e-mail só antes do aceite (D-035).
- [x] Ativar/inativar vínculo (2026-11-05): D-037.
- [x] Definir valor e vencimento conforme regras aprovadas (2026-11-05): D-036.
  Critério: personal gerencia somente alunos associados a ele.

## Fase 3 — Aulas
- [x] Registrar aula (2026-11-09): D-039 e aviso de possível duplicada (D-040).
- [x] Listar aulas e filtrar por mês/estado/aluno (2026-11-09).
- [x] Detalhar aula (2026-11-09): visão do personal e do aluno; aula não editável (D-041).
- [x] Reconhecer/aprovar aula pelo aluno (2026-11-09): "Confirmar" (D-038), resposta única (D-041).
- [x] Recusar aula pelo aluno (2026-11-09): motivo obrigatório até 500 caracteres, com sugestões.
- [x] Excluir aula pelo personal com auditoria e motivo quando exigido (2026-11-09): D-042.
  Critério: transições de estado e permissões testadas; histórico consistente.

## Fase 4 — Demonstrativo e faturamento
- [x] Definir período de cobrança e regras de cálculo (2026-11-10): D-043, D-044, D-046.
- [x] Gerar demonstrativo com snapshot (2026-11-10): fechamento por `BackgroundService` com advisory lock (D-047) e comando `close-billing`.
- [x] Visualizar aulas e total do período (2026-11-10): demonstrativo para o personal e para o aluno (D-048).
- [x] Consultar faturamento mensal do personal (2026-11-10): `/personal/financeiro` por mês de vencimento, com prévia do que ainda vai fechar.
- [x] Crédito no próximo demonstrativo para aula excluída depois de cobrada em período fechado (2026-11-10): D-045.
  Critério: cálculo reproduzível, rastreável e coberto por testes.

## Fase 5 — Pagamentos informados
- [x] Aluno informa pagamento (2026-11-10): valor total, observação opcional, informar de novo após recusa (D-049).
- [x] Personal recebe item pendente de confirmação (2026-11-10): "Pagamentos para confirmar" no Financeiro e contador no Início.
- [x] Personal confirma recebimento (2026-11-10): pela lista ou pelo detalhe; "Não recebi" com mensagem; registro direto (D-050 a D-052).
- [x] Histórico de pagamentos (2026-11-10): linha do tempo no demonstrativo do aluno e do personal (D-053).
  Critério: período só muda para pago após confirmação do personal.

## Fase 6 — WhatsApp
- [x] Comparar provedores e escolher um com aprovação (2026-11-12): Meta WhatsApp Cloud API direta (D-055).
- [x] Implementar adapter (2026-11-12): `IWhatsAppSender` com Meta e Log; templates em `WHATSAPP_TEMPLATES.md`.
- [x] Implementar outbox e worker (2026-11-12): `SKIP LOCKED`, lease, backoff, silêncio 22h–8h (D-061, D-062).
- [x] Notificar nova aula (2026-11-12): `ph_nova_aula`.
- [x] Notificar personal de aprovação/recusa (2026-11-12): `ph_aula_confirmada`, `ph_aula_recusada`.
- [x] Notificar aluno sobre exclusão relevante (2026-11-12): `ph_aula_excluida`, só para aula já respondida.
- [x] Notificar aluno no vencimento com demonstrativo (2026-11-12): `ph_demonstrativo_emitido`, junto com o fechamento.
- [x] Notificar personal quando aluno informar pagamento (2026-11-12): `ph_pagamento_informado`.
- [x] Notificar aluno quando o personal confirmar ou recusar ("Não recebi") o pagamento (2026-11-12): `ph_pagamento_confirmado`, `ph_pagamento_nao_recebido` (D-054).
- [x] Implementar webhooks/links seguros se necessários (2026-11-12): webhook com assinatura `X-Hub-Signature-256`; botões levam ao app logado.
- [x] Exigir código de uso único, enviado por WhatsApp ao celular do aluno, no aceite do convite (2026-11-12): D-056, D-057.
- [x] Recuperação de senha por WhatsApp (2026-11-12): D-058; alunos antigos confirmam o celular (D-059).
  Critério: falhas, retries, duplicidade e auditoria testados.
  Pendente fora do código: conta da Meta liberada, 9 templates aprovados e segredos configurados (README, "Configurar WhatsApp").

## Fase 7 — Dashboard e acabamento
- [ ] Home do personal com aulas, pendências e faturamento.
- [ ] Home do aluno com resumo, aulas pendentes e pagamento.
- [ ] Responsividade e acessibilidade.
- [ ] Revisão de segurança.
- [ ] Testes ponta a ponta dos fluxos críticos.
- [ ] Documentação de execução e operação.

## Definition of Done por feature

- [ ] Requisitos e critérios de aceite aprovados.
- [ ] Design aprovado antes do código.
- [ ] Implementação respeita arquitetura e segurança.
- [ ] Validações e autorização no servidor.
- [ ] Testes unitários/integrados relevantes.
- [ ] Build sem erros.
- [ ] Estados de UI cobertos.
- [ ] Logs sem dados sensíveis.
- [ ] Documentação atualizada.
- [ ] Passos de teste manual fornecidos.
- [ ] Proprietário aceitou a entrega.