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
- [ ] Autorização e isolamento de dados.
  Critério: usuários autenticados acessam apenas funcionalidades e dados do próprio perfil/vínculo.
  Em revisão (2026-11-04): autorização por padrão (FallbackPolicy), policy `ActivePersonal` checada no banco a cada requisição e link do convite a partir de `App:PublicBaseUrl`.

## Fase 2 — Alunos
- [ ] Listar alunos.
- [ ] Cadastrar aluno. Entregue em parte na Fase 1 (cadastro + convite); faltam reenvio/revogação do convite e a ligação com a lista e os detalhes.
- [ ] Consultar detalhes.
- [ ] Editar dados permitidos.
- [ ] Ativar/inativar vínculo.
- [ ] Definir valor e vencimento conforme regras aprovadas.
  Critério: personal gerencia somente alunos associados a ele.

## Fase 3 — Aulas
- [ ] Registrar aula.
- [ ] Listar aulas e filtrar por mês/estado/aluno.
- [ ] Detalhar aula.
- [ ] Reconhecer/aprovar aula pelo aluno.
- [ ] Recusar aula pelo aluno.
- [ ] Excluir aula pelo personal com auditoria e motivo quando exigido.
  Critério: transições de estado e permissões testadas; histórico consistente.

## Fase 4 — Demonstrativo e faturamento
- [ ] Definir período de cobrança e regras de cálculo.
- [ ] Gerar demonstrativo com snapshot.
- [ ] Visualizar aulas e total do período.
- [ ] Consultar faturamento mensal do personal.
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