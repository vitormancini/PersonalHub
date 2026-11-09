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
- [ ] Definir período de cobrança e regras de cálculo.
- [ ] Gerar demonstrativo com snapshot.
- [ ] Visualizar aulas e total do período.
- [ ] Consultar faturamento mensal do personal.
- [ ] Crédito no próximo demonstrativo para aula excluída depois de cobrada em período fechado (D-023, pendente desde a Fase 3/D-042).
  Critério: cálculo reproduzível, rastreável e coberto por testes.

## Fase 5 — Pagamentos informados
- [ ] Aluno informa pagamento.
- [ ] Personal recebe item pendente de confirmação.
- [ ] Personal confirma recebimento.
- [ ] Histórico de pagamentos.
  Critério: período só muda para pago após confirmação do personal.

## Fase 6 — WhatsApp
- [ ] Comparar provedores e escolher um com aprovação.
- [ ] Implementar adapter.
- [ ] Implementar outbox e worker.
- [ ] Notificar nova aula.
- [ ] Notificar personal de aprovação/recusa.
- [ ] Notificar aluno sobre exclusão relevante.
- [ ] Notificar aluno no vencimento com demonstrativo.
- [ ] Notificar personal quando aluno informar pagamento.
- [ ] Implementar webhooks/links seguros se necessários.
- [ ] Exigir código de uso único, enviado por WhatsApp ao celular do aluno, no aceite do convite (D-033).
  Critério: falhas, retries, duplicidade e auditoria testados.

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