# PersonalHub — Registro de decisões

Use este documento para registrar decisões aprovadas. Não apagar decisões antigas; quando uma decisão mudar, registrar a nova decisão e a data, explicando o motivo.

## Decisões confirmadas

| ID | Tema | Decisão |
|---|---|---|
| D-001 | Nome | PersonalHub |
| D-002 | Plataforma | .NET 10 |
| D-003 | UI | Blazor Server / Interactive Server |
| D-004 | Arquitetura | Vertical Slice Architecture, feature based |
| D-005 | Banco | PostgreSQL |
| D-006 | Desenvolvimento local | PostgreSQL executado via Docker |
| D-007 | CSS | Bootstrap |
| D-008 | JavaScript | jQuery disponível; usar quando necessário |
| D-009 | Responsividade | Mobile first |
| D-010 | Hospedagem | AWS; serviços ainda não definidos |
| D-011 | MVP | Sem planos de assinatura nem gateway para cobrança do personal |
| D-012 | Provisionamento | Personais cadastrados manualmente para o MVP |
| D-013 | Design | Design de cada feature deve ser aprovado antes da implementação |
| D-014 | Marca | Azul-marinho, slate/cinza-escuro, branco e azul vibrante |
| D-015 | Relação personal/aluno | Um aluno pode ter mais de um personal (resolve P-001) |
| D-016 | Conta do aluno | Criada por convite com link de uso único válido por 7 dias (resolve P-002 e P-015) |
| D-017 | Dados pessoais | Sem CPF no MVP; nome, e-mail e celular obrigatórios (resolve P-003) |
| D-018 | Preço | Cobrança por aula; preço da aula congelado no registro (resolve P-004) |
| D-019 | Período | Mês calendário; vencimento cobra o mês anterior (resolve P-005) |
| D-020 | Aula cobrável | Somente aula aprovada (resolve P-006) |
| D-021 | Recusa | Motivo obrigatório; correção por exclusão e novo registro (resolve P-007) |
| D-022 | Prazo | Sem prazo de resposta no MVP (resolve P-008) |
| D-023 | Exclusão após fechamento | Período fechado não muda; crédito no próximo demonstrativo (resolve P-009) |
| D-024 | Pagamento | Um pagamento integral por período; sem parcial (resolve P-010) |
| D-025 | Vencimento | Dia inexistente no mês usa o último dia do mês (resolve P-011) |
| D-026 | Fuso | `America/Sao_Paulo`; produto somente Brasil (resolve P-012) |
| D-027 | Admin | Personal provisionado por script de linha de comando versionado (resolve P-016) |
| D-028 | Fim do vínculo | Vínculo inativado, sem apagar histórico (resolve P-017 em parte) |
| D-029 | Modelo de dados | Modelo relacional descrito em `DOMAIN_AND_DATABASE.md` |
| D-030 | Recuperação de senha | Via WhatsApp na Fase 6; sem link no login até lá |
| D-031 | Envio do convite | Pelo WhatsApp do próprio personal (link wa.me com mensagem pronta) até a Fase 6 |
| D-032 | Nome do aluno na conta | Coluna `FullName` em `AspNetUsers`, preenchida pelo aluno no aceite |
| D-033 | Aceite do convite | Na Fase 6, exige código de uso único enviado por WhatsApp ao celular do aluno; risco aceito no MVP |
| D-034 | Reenvio do convite | Gera novo link e revoga o anterior na hora; há também "Cancelar convite" sem gerar outro |
| D-035 | E-mail do aluno | Editável só enquanto `Invited` (trocar revoga o convite); depois do aceite é o login e fica somente leitura |
| D-036 | Alteração de valor e vencimento | Valor novo vale para aulas registradas a partir da alteração; vencimento novo, a partir do próximo demonstrativo |
| D-037 | Inativar e reativar vínculo | Inativar mantém histórico e revoga convite; reativar volta para `Active` (com conta) ou `Invited` (novo link) |

## Pendências que exigem decisão

| ID | Tema | Pergunta | Situação |
|---|---|---|---|
| P-001 | Relação personal/aluno | Um aluno pode ter mais de um personal? | Resolvida em D-015 |
| P-002 | Conta do aluno | Conta criada no cadastro ou convite posterior? | Resolvida em D-016 |
| P-003 | Dados pessoais | CPF é obrigatório? Quais campos são realmente necessários? | Resolvida em D-017 |
| P-004 | Preço | Cobrança por aula, mensalidade fixa ou opção configurável? | Resolvida em D-018 |
| P-005 | Período | Mês calendário ou ciclo entre vencimentos? | Resolvida em D-019 |
| P-006 | Aula cobrável | Aula pendente entra no demonstrativo ou somente aprovada? | Resolvida em D-020 |
| P-007 | Recusa | É obrigatório informar motivo? Como resolver aula recusada? | Resolvida em D-021 |
| P-008 | Prazo | Existe prazo para aluno reconhecer/recusar? | Resolvida em D-022 |
| P-009 | Exclusão | Como ajustar período já fechado se aula for excluída? | Resolvida em D-023 |
| P-010 | Pagamento | Permitir pagamento parcial ou múltiplos pagamentos? | Resolvida em D-024 |
| P-011 | Vencimento | Regra para dias 29, 30 e 31 em meses curtos? | Resolvida em D-025 |
| P-012 | Fuso | Confirmar `America/Sao_Paulo` como fuso do MVP. | Resolvida em D-026 |
| P-013 | WhatsApp | Qual provedor será utilizado? | Aberta (Fase 6) |
| P-014 | WhatsApp | Como validar reconhecimento via WhatsApp: link autenticado, resposta ou ambos? | Aberta (Fase 6) |
| P-015 | Convites | Como o aluno recebe credenciais/acesso inicial? | Resolvida em D-016 |
| P-016 | Admin | Provisionamento de personal por SQL/seed/script ou ferramenta administrativa mínima? | Resolvida em D-027 |
| P-017 | Exclusão | Aluno e personal podem encerrar vínculo? Qual política de retenção? | Parcial em D-028; prazo de retenção ainda aberto |
| P-018 | Notificação | Quais eventos também terão notificação dentro da aplicação? | Aberta (Fase 6) |

## Registro detalhado

### D-015 — Aluno com mais de um personal
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-001.
- Decisão: um aluno pode ter vínculos com vários personais. Cada par personal–aluno é um `PersonalStudent` único, com preço e vencimento próprios.
- Alternativas consideradas: vínculo único ativo por aluno.
- Consequências: o vínculo guarda nome, e-mail e celular que o personal digitou; a conta do aluno é só a identidade de login. Um personal nunca vê dados de outro vínculo.
- Aprovado por: Vítor Mancini

### D-016 — Acesso do aluno por convite
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-002 e P-015.
- Decisão: o personal cadastra o aluno, que recebe um link de uso único válido por 7 dias e cria a própria senha. Se o e-mail já tiver conta, o aceite pede login e só liga o vínculo.
- Alternativas consideradas: conta criada no cadastro com senha temporária.
- Consequências: tabela `StudentInvite` com hash SHA-256 do token; vínculo começa em `Invited`.
- Aprovado por: Vítor Mancini

### D-017 — Dados pessoais mínimos
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-003.
- Decisão: sem CPF no MVP. Nome, e-mail (login) e celular (WhatsApp) obrigatórios.
- Alternativas consideradas: CPF opcional ou obrigatório.
- Consequências: nenhum dado exige criptografia em nível de aplicação no MVP.
- Aprovado por: Vítor Mancini

### D-018 — Cobrança por aula
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-004.
- Decisão: preço por aula definido no vínculo. Cada aula copia o preço vigente em `UnitPrice` no momento do registro.
- Alternativas consideradas: mensalidade fixa; modelo configurável.
- Consequências: mudar o preço do vínculo não altera aulas já registradas. Mensalidade fica fora do MVP.
- Aprovado por: Vítor Mancini

### D-019 — Período em mês calendário
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-005.
- Decisão: o demonstrativo emitido no vencimento do dia X cobra as aulas do mês calendário anterior.
- Alternativas consideradas: ciclo entre vencimentos.
- Consequências: `BillingPeriod.ReferenceMonth` único por vínculo, o que torna o fechamento idempotente.
- Aprovado por: Vítor Mancini

### D-020 — Somente aula aprovada é cobrada
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-006.
- Decisão: só aulas aprovadas entram no demonstrativo. Uma aula pendente aprovada depois do fechamento entra no fechamento seguinte.
- Consequências: índice único `(LessonId, Kind)` em `BillingPeriodItem` impede cobrança dupla.
- Aprovado por: Vítor Mancini

### D-021 — Recusa com motivo
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-007.
- Decisão: motivo curto obrigatório na recusa. Para corrigir, o personal exclui a aula e registra outra.
- Consequências: `RejectionReason` obrigatório quando `Status = Rejected`.
- Aprovado por: Vítor Mancini

### D-022 — Sem prazo de resposta
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-008.
- Decisão: sem prazo automático no MVP. Aulas pendentes aparecem no demonstrativo como não cobradas.
- Aprovado por: Vítor Mancini

### D-023 — Crédito por exclusão após fechamento
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-009.
- Decisão: demonstrativo emitido nunca é alterado. Excluir aula já cobrada gera um item de crédito (valor negativo) no próximo demonstrativo. Se os créditos superarem o total, o saldo segue para o mês seguinte.
- Alternativas consideradas: reabrir o período; recalcular o total.
- Aprovado por: Vítor Mancini

### D-024 — Pagamento único e integral
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-010.
- Decisão: um pagamento do valor total por período. Se o personal recusar o aviso, o aluno pode informar de novo.
- Consequências: `Payment` 1:N para histórico, com no máximo um pagamento `Reported` ou `Confirmed` por período.
- Aprovado por: Vítor Mancini

### D-025 — Vencimento em mês curto
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-011.
- Decisão: quando o dia de vencimento não existe no mês, usa-se o último dia do mês.
- Aprovado por: Vítor Mancini

### D-026 — Fuso horário
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-012.
- Decisão: `America/Sao_Paulo`; produto somente Brasil. Instantes em UTC (`timestamptz`), datas de período em `date` local.
- Aprovado por: Vítor Mancini

### D-027 — Provisionamento de personal por script
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-016.
- Decisão: script de linha de comando versionado cria usuário, role `Personal` e `PersonalProfile` numa transação. Sem tela administrativa no MVP.
- Aprovado por: Vítor Mancini

### D-028 — Encerramento de vínculo
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: P-017.
- Decisão: encerrar vínculo muda `Status` para `Inactive` e preenche `EndedAtUtc`; nenhum histórico é apagado. O prazo de retenção de dados continua aberto.
- Aprovado por: Vítor Mancini

### D-029 — Modelo de dados
- Data: 2026-11-03
- Estado: Aprovada
- Contexto: Fase 0 do roadmap.
- Decisão: modelo descrito em `DOMAIN_AND_DATABASE.md`: chaves `uuid`, dinheiro `numeric(12,2)`, concorrência otimista por `xmin`, exclusão de aula como `DeletedAtUtc` separado da decisão do aluno.
- Aprovado por: Vítor Mancini

### D-030 — Recuperação de senha via WhatsApp
- Data: 2026-11-04
- Estado: Aprovada
- Contexto: design da tela de login (Fase 1).
- Decisão: a recuperação de senha será feita via WhatsApp na Fase 6. Até lá, a tela de login não exibe o link "Esqueci minha senha".
- Alternativas consideradas: recuperação por e-mail desde a Fase 1.
- Consequências: até a Fase 6, quem esquecer a senha depende de suporte manual; o fluxo entra junto com o provedor de WhatsApp (P-013).
- Aprovado por: Vítor Mancini

### D-031 — Convite enviado pelo WhatsApp do próprio personal
- Data: 2026-11-04
- Estado: Aprovada
- Contexto: design do fluxo de cadastro e convite do aluno (Fase 1). O provedor de WhatsApp (P-013) só será escolhido na Fase 6.
- Decisão: até a Fase 6, o convite é enviado pelo WhatsApp do próprio personal: a tela mostra o link e um botão que abre `https://wa.me/<número>?text=<mensagem pronta>`. O PersonalHub não envia nada sozinho.
- Alternativas consideradas: envio por e-mail; esperar o provedor de WhatsApp.
- Consequências: o link só aparece uma vez, logo após o cadastro (o token não é gravado). Reenvio fica para a Fase 2. Na Fase 6 o envio pode passar para a outbox sem mudar o modelo do convite.
- Aprovado por: Vítor Mancini

### D-032 — Nome informado pelo aluno na conta
- Data: 2026-11-04
- Estado: Aprovada
- Contexto: a tela de aceite do convite tem o campo "Seu nome", editável. O vínculo guarda o nome que o personal digitou (D-015) e não deve ser sobrescrito pelo aluno.
- Decisão: nova coluna `FullName varchar(150) null` em `AspNetUsers` (migration `AddUserFullName`), preenchida pelo aluno ao criar a conta. Nula para personais (nome em `PersonalProfile`).
- Alternativas consideradas: atualizar `PersonalStudent.StudentName` (quebraria D-015); descartar o nome digitado.
- Consequências: cada personal continua vendo o nome que cadastrou; o nome da conta servirá à área do aluno.
- Aprovado por: Vítor Mancini

### D-033 — Verificação de posse do celular no aceite do convite
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: hoje o personal tem o link do convite (D-031) e poderia aceitá-lo ele mesmo, criando a conta do aluno e aprovando aulas em nome dele na Fase 3.
- Decisão: na Fase 6, o aceite do convite passa a exigir um código de uso único enviado por WhatsApp ao celular do aluno. O personal não vê esse código. Até lá, o risco fica aceito no MVP.
- Alternativas consideradas: manter o aceite só com o link; verificar por e-mail.
- Consequências: item adicionado à Fase 6 do roadmap e nota de risco aceito em `SECURITY.md` (seção 8). Até a Fase 6, a aprovação de aulas não prova que foi o aluno quem aprovou.
- Aprovado por: Vítor Mancini

### D-034 — Reenvio e cancelamento do convite
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: design da Fase 2 (gestão de alunos). D-031 deixou o reenvio para a Fase 2. Só o hash do token é gravado (D-016), então o link original não pode ser mostrado de novo.
- Decisão: "Gerar novo link e enviar" cria um convite novo de 7 dias e revoga na hora todos os convites em aberto do vínculo. O link aparece uma única vez, na mesma tela "Convite do aluno" do cadastro. "Cancelar convite" só revoga, sem gerar outro.
- Alternativas consideradas: guardar o token para reexibir o mesmo link; manter vários links válidos ao mesmo tempo.
- Consequências: no máximo um convite válido por vínculo; um link vazado deixa de valer assim que o personal gera outro ou cancela. Ações auditadas como `StudentInviteRenewed` e `StudentInviteCancelled`.
- Aprovado por: Vítor Mancini

### D-035 — E-mail do aluno editável só antes do aceite
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: o e-mail do vínculo vira o login do aluno quando ele aceita o convite.
- Decisão: o personal edita o e-mail só enquanto o vínculo está `Invited`; trocar o e-mail revoga o convite em aberto e gera um novo link na mesma transação. Depois do aceite (`Active` ou `Inactive`), o e-mail fica somente leitura para o personal: é o login e só o aluno pode trocar. O servidor ignora o e-mail enviado nesses casos.
- Alternativas consideradas: permitir troca a qualquer momento; bloquear a troca também antes do aceite.
- Consequências: o personal corrige um e-mail digitado errado sem recadastrar o aluno; a troca de login pelo próprio aluno fica para a área do aluno.
- Aprovado por: Vítor Mancini

### D-036 — Vigência de novo valor da aula e novo vencimento
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: edição dos dados de cobrança do vínculo.
- Decisão: o novo valor da aula vale só para aulas registradas a partir da alteração (o preço já é congelado por aula, D-018). O novo dia de vencimento vale a partir do próximo demonstrativo.
- Alternativas consideradas: recalcular aulas pendentes; agendar a alteração para o mês seguinte.
- Consequências: nenhum valor histórico muda. A auditoria `StudentUpdated` guarda os campos alterados e os valores anterior/novo de preço e vencimento (sem nome, e-mail ou celular).
- Aprovado por: Vítor Mancini

### D-037 — Inativar e reativar o vínculo
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: D-028 definiu o encerramento; faltava a reativação.
- Decisão: inativar mantém todo o histórico, impede novas aulas, revoga o convite em aberto e preenche `EndedAtUtc`. Reativar volta para `Active` se `StudentUserId` existe; senão volta para `Invited` com um novo link de convite gerado na hora. `EndedAtUtc` volta a ser nulo. A reativação é bloqueada se o personal tiver outro vínculo não inativo com o mesmo e-mail (o índice único existente também garante).
- Alternativas consideradas: reativar sempre como `Invited`; criar um vínculo novo em vez de reativar.
- Consequências: o personal retoma um aluno sem perder o histórico; o bloqueio por e-mail evita dois vínculos ativos para a mesma pessoa. Ações auditadas como `StudentLinkInactivated` e `StudentLinkReactivated`.
- Aprovado por: Vítor Mancini

## Modelo para novas decisões

### D-XXX — Título
- Data:
- Estado: Proposta / Aprovada / Substituída
- Contexto:
- Decisão:
- Alternativas consideradas:
- Consequências:
- Aprovado por:
