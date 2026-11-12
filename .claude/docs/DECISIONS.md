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
| D-030 | Recuperação de senha | Via WhatsApp na Fase 6; sem link no login até lá (cumprida em D-058) |
| D-031 | Envio do convite | Pelo WhatsApp do próprio personal (link wa.me com mensagem pronta) até a Fase 6 |
| D-032 | Nome do aluno na conta | Coluna `FullName` em `AspNetUsers`, preenchida pelo aluno no aceite |
| D-033 | Aceite do convite | Na Fase 6, exige código de uso único enviado por WhatsApp ao celular do aluno; risco aceito no MVP (cumprida em D-057) |
| D-034 | Reenvio do convite | Gera novo link e revoga o anterior na hora; há também "Cancelar convite" sem gerar outro |
| D-035 | E-mail do aluno | Editável só enquanto `Invited` (trocar revoga o convite); depois do aceite é o login e fica somente leitura |
| D-036 | Alteração de valor e vencimento | Valor novo vale para aulas registradas a partir da alteração; vencimento novo, a partir do próximo demonstrativo |
| D-037 | Inativar e reativar vínculo | Inativar mantém histórico e revoga convite; reativar volta para `Active` (com conta) ou `Invited` (novo link) |
| D-038 | Rótulos das aulas | `Pending`/`Approved`/`Rejected` aparecem como "Aguardando"/"Confirmada"/"Recusada"; a ação do aluno é "Confirmar" |
| D-039 | Registro de aula | Sem data/hora no futuro; retroativa permitida; vínculo `Invited` recebe aula, `Inactive` não; duração e observação opcionais; preço copiado do vínculo |
| D-040 | Possível duplicada | Mesmo vínculo, data e horário mostra aviso e exige "Registrar mesmo assim"; não bloqueia |
| D-041 | Aula não editável | Correção = excluir e registrar outra; o aluno responde uma única vez |
| D-042 | Exclusão lógica da aula | Pendente: motivo opcional e some para o aluno; respondida: motivo obrigatório e o aluno vê "Excluída" com o motivo |
| D-043 | Fechamento do demonstrativo | No vencimento (00:00 de São Paulo) emite o mês anterior com as aulas `Approved` ainda não cobradas; pendentes ficam de fora |
| D-044 | Mês sem cobrança e total zero | Sem aula nem crédito, não emite; créditos que zeram o total emitem já `Paid` e levam o saldo adiante |
| D-045 | Crédito por aula excluída depois de cobrada | Item `Adjustment` negativo no próximo demonstrativo; o emitido nunca muda |
| D-046 | Vínculo inativo | Ainda fecha no próximo vencimento o que estiver pendente de cobrança |
| D-047 | Agendamento do fechamento | `BackgroundService` a cada hora, idempotente, com advisory lock; comando `close-billing` |
| D-048 | Rótulos do demonstrativo | `Issued` = "Aguardando pagamento", `Paid` = "Pago" (`BillingDisplay`) |
| D-049 | Aluno informa pagamento | Valor total do demonstrativo `Issued`, sem pagamento ativo; observação opcional até 500; período vai para "Pagamento informado" |
| D-050 | Confirmação | Pela lista do Financeiro ou pelo detalhe; `Payment` `Confirmed` e período `Paid` na mesma transação |
| D-051 | "Não recebi" | `Payment` `Rejected` com mensagem opcional (até 500, visível ao aluno); período volta a `Issued` e o aluno pode informar de novo |
| D-052 | Registro direto | Personal marca como pago um demonstrativo `Issued` sem aviso do aluno (`Payment` `Confirmed` registrado pelo personal) |
| D-053 | Histórico do pagamento | Demonstrativo mostra emitido, informado(s), recusado(s) e confirmado; confirmação definitiva |
| D-054 | Avisos de pagamento | WhatsApp de informado/confirmado/recusado fica para a Fase 6; até lá, pendências no Financeiro |
| D-055 | Provedor de WhatsApp | Meta WhatsApp Cloud API direta, sem intermediário; templates em `pt_BR` (resolve P-013) |
| D-056 | Código por WhatsApp | 6 dígitos, 10 minutos, 5 tentativas; reenvio após 60 s; até 3 envios por hora por celular; bloqueio de 15 minutos |
| D-057 | Aceite do convite com código | Código ao celular cadastrado pelo personal antes de criar a conta ou aceitar com conta existente; confirma o celular da conta (fecha D-033) |
| D-058 | Recuperação de senha | "Esqueci minha senha" com código no celular confirmado; resposta sempre igual; troca o security stamp (fecha D-030) |
| D-059 | Alunos de antes da Fase 6 | Confirmam o celular do vínculo mais recente logo após o login, antes de usar o app |
| D-060 | Conteúdo das mensagens | Nenhum texto livre digitado por usuário vai no WhatsApp; a mensagem avisa e leva ao app |
| D-061 | Horário de silêncio | Avisos entre 22:00 e 08:00 (São Paulo) saem às 08:00; códigos saem na hora |
| D-062 | Botão e retentativas | Todo aviso tem botão de URL para a tela certa; até 5 tentativas (1 min, 5 min, 30 min, 2 h) e depois `Failed` (resolve P-014) |
| D-063 | Envio do convite | Continua pelo WhatsApp do próprio personal (wa.me), como em D-031 |

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
| P-013 | WhatsApp | Qual provedor será utilizado? | Resolvida em D-055 |
| P-014 | WhatsApp | Como validar reconhecimento via WhatsApp: link autenticado, resposta ou ambos? | Resolvida em D-062: o botão abre o app logado; a ação acontece só no app |
| P-015 | Convites | Como o aluno recebe credenciais/acesso inicial? | Resolvida em D-016 |
| P-016 | Admin | Provisionamento de personal por SQL/seed/script ou ferramenta administrativa mínima? | Resolvida em D-027 |
| P-017 | Exclusão | Aluno e personal podem encerrar vínculo? Qual política de retenção? | Parcial em D-028; prazo de retenção ainda aberto |
| P-018 | Notificação | Quais eventos também terão notificação dentro da aplicação? | Aberta (Fase 7): a Fase 6 só envia WhatsApp; as pendências continuam no Início e no Financeiro |

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

### D-038 — Rótulos das situações da aula
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: os nomes técnicos `Pending`, `Approved` e `Rejected` não servem para a tela; "aprovar" soa burocrático para o aluno.
- Decisão: na interface, `Pending` aparece como "Aguardando", `Approved` como "Confirmada" e `Rejected` como "Recusada"; aula excluída aparece como "Excluída". A ação do aluno é "Confirmar" (e "Recusar"). Os nomes no banco e no código não mudam.
- Alternativas consideradas: "Pendente/Aprovada/Reprovada"; "Reconhecer aula".
- Consequências: os rótulos ficam centralizados (`LessonDisplay`) e são os mesmos nas telas do personal e do aluno.
- Aprovado por: Vítor Mancini

### D-039 — Regras do registro de aula
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: o personal registra a aula depois de dá-la; o aluno pode ainda não ter aceitado o convite.
- Decisão: data e hora não podem estar no futuro (relógio de `America/Sao_Paulo`, D-026); registro retroativo é permitido. Vínculo `Invited` pode receber aula, que fica `Pending` até o aluno criar a conta e responder; vínculo `Inactive` não recebe aula. Duração opcional (30, 45, 60 ou 90 minutos). Observação opcional, até 500 caracteres, visível ao aluno. `UnitPrice` é copiado de `LessonPrice` do vínculo no momento do registro (D-018).
- Alternativas consideradas: bloquear registro para vínculo `Invited`; limitar o registro retroativo; duração livre.
- Consequências: o personal não precisa esperar o aceite para registrar; mudar o preço do vínculo não altera aulas já registradas (D-036).
- Aprovado por: Vítor Mancini

### D-040 — Aviso de possível aula duplicada
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: duplo envio ou registro repetido do mesmo treino.
- Decisão: se já existe aula não excluída do mesmo vínculo na mesma data e horário, a tela mostra um aviso e exige a confirmação "Registrar mesmo assim". Não há bloqueio nem restrição única no banco.
- Alternativas consideradas: bloquear a duplicada; não avisar.
- Consequências: casos legítimos (duas sessões no mesmo horário registradas por engano de horário, por exemplo) continuam possíveis; o aluno ainda pode recusar uma duplicada.
- Aprovado por: Vítor Mancini

### D-041 — Aula não editável e resposta única
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: editar uma aula já vista ou respondida pelo aluno quebraria a confiança no histórico.
- Decisão: a aula não é editável; a correção é excluir e registrar outra (reforça D-021). O aluno responde uma única vez (confirmar ou recusar); a resposta é definitiva.
- Alternativas consideradas: edição enquanto `Pending`; permitir ao aluno mudar a resposta.
- Consequências: a resposta é conferida com a aula travada (`SELECT ... FOR UPDATE`); uma segunda resposta recebe mensagem amigável e não altera nada.
- Aprovado por: Vítor Mancini

### D-042 — Exclusão lógica da aula
- Data: 2026-11-05
- Estado: Aprovada
- Contexto: o personal precisa corrigir registros sem apagar histórico.
- Decisão: a exclusão é lógica (`DeletedAtUtc`, `DeletedByUserId`, `DeletionReason`). Aula `Pending`: motivo opcional e a aula some para o aluno. Aula `Approved` ou `Rejected`: motivo obrigatório e o aluno vê a aula como "Excluída" com o motivo. A regra é conferida com a aula travada: se o aluno respondeu enquanto o personal confirmava a exclusão, o pedido sem motivo volta pedindo um.
- Alternativas consideradas: exclusão física; motivo sempre obrigatório.
- Consequências: aula excluída não entra em demonstrativo. O crédito de aula já cobrada em período fechado (D-023) fica para a Fase 4. Ação auditada como `LessonDeleted` (sem o motivo no metadata).
- Aprovado por: Vítor Mancini

### D-043 — Fechamento do demonstrativo
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: D-019 definiu o período; faltava dizer o que entra e quando.
- Decisão: no dia do vencimento (`DueDate` ajustado por D-025), a partir de 00:00 de `America/Sao_Paulo`, emite o demonstrativo do mês anterior (`ReferenceMonth`). Itens: todas as aulas `Approved`, não excluídas, com `OccurredAt` antes do fim do mês de referência e ainda não cobradas (inclui confirmadas com atraso de meses anteriores). Aulas `Pending` ficam de fora e o demonstrativo informa quantas aguardam. `Description` e `Amount` são congelados (`UnitPrice` da aula).
- Alternativas consideradas: cobrar só aulas do próprio mês; esperar as pendentes.
- Consequências: o corte é a meia-noite do vencimento: entra o que foi confirmado antes dela e vira crédito o que foi excluído antes dela. Assim o resultado não depende da hora em que o job roda (inclusive ao recuperar dias perdidos). A quantidade de aulas aguardando é calculada na hora da consulta, não gravada.
- Aprovado por: Vítor Mancini

### D-044 — Mês sem cobrança e total zero
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: meses sem aula e créditos maiores que o total.
- Decisão: sem aula a cobrar e sem crédito, não emite demonstrativo naquele mês. Créditos que zeram ou passam do total: emite com `TotalAmount` 0 já `Paid` e leva o saldo para o próximo (D-023).
- Alternativas consideradas: emitir demonstrativo zerado todo mês.
- Consequências: o saldo levado adiante vira um item `Adjustment` sem `LessonId` ("Saldo de crédito de {mês}") no próximo demonstrativo. Só o saldo, sem aula nem crédito novo, não dispara emissão: ele espera o próximo demonstrativo com movimento (interpretação registrada na implementação da Fase 4).
- Aprovado por: Vítor Mancini

### D-045 — Crédito por aula excluída depois de cobrada
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: implementa D-023 e a pendência de D-042.
- Decisão: excluir aula já cobrada em demonstrativo emitido gera item `Adjustment` negativo no próximo demonstrativo do vínculo; o emitido nunca muda.
- Alternativas consideradas: reabrir o demonstrativo; recalcular o total.
- Consequências: o crédito é gerado no fechamento seguinte (a exclusão não grava nada no faturamento). O índice único `(LessonId, Kind)` impede creditar duas vezes. O detalhe da aula excluída avisa que ela volta como crédito.
- Aprovado por: Vítor Mancini

### D-046 — Vínculo inativo
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: vínculo inativado com aulas ainda não cobradas.
- Decisão: vínculo inativo ainda fecha no próximo vencimento o que estiver pendente de cobrança.
- Consequências: depois disso, sem aula a cobrar, não há novos demonstrativos (D-044).
- Aprovado por: Vítor Mancini

### D-047 — Agendamento do fechamento
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: ARCHITECTURE §7 pedia comparar `BackgroundService` e agendador gerenciado.
- Decisão: `BackgroundService` no próprio app verifica a cada hora (e na partida) os vínculos com fechamento devido, inclusive dias perdidos enquanto o app esteve fora; idempotente pelos índices únicos `(PersonalStudentId, ReferenceMonth)` e `(LessonId, Kind)`, em transação por vínculo, com advisory lock do Postgres para não rodar em paralelo (mais de uma instância na AWS). Logs estruturados sem dados pessoais. Sem serviço externo no MVP.
- Alternativas consideradas: EventBridge Scheduler/cron chamando um endpoint; Hangfire/Quartz.
- Consequências: com o app fora por mais de um vencimento, fecha só o mês mais recente vencido, que leva as aulas dos meses anteriores; nunca fecha um mês anterior a um demonstrativo existente. Há o comando `close-billing` (com `--date` só em Development) para rodar o fechamento manualmente.
- Aprovado por: Vítor Mancini

### D-048 — Rótulos do demonstrativo
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: nomes técnicos de `BillingPeriodStatus`.
- Decisão: `Issued` aparece como "Aguardando pagamento", `Paid` como "Pago" (`PaymentReported` fica para a Fase 5, como "Pagamento informado", D-049). Nesta fase não há como marcar pago, exceto total zero.
- Consequências: rótulos centralizados em `BillingDisplay`.
- Aprovado por: Vítor Mancini

### D-049 — Aluno informa pagamento
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: Fase 5; D-024 definiu um pagamento integral por período.
- Decisão: o aluno informa o pagamento do valor total do demonstrativo (sem parcial), com observação opcional até 500 caracteres. Só em demonstrativo `Issued` e sem pagamento ativo. O período vai para `PaymentReported` ("Pagamento informado").
- Alternativas consideradas: pagamento parcial; comprovante anexado.
- Consequências: vínculo inativo ainda pode informar. Total zero já nasce `Paid` e não aceita aviso. Informar duas vezes (ou em paralelo) cria um único `Payment`: o período é travado com `FOR UPDATE` e o índice único de pagamento ativo segura o resto. Auditado como `PaymentReported`, sem a observação.
- Aprovado por: Vítor Mancini

### D-050 — Confirmação do recebimento
- Data: 2026-11-10
- Estado: Aprovada
- Decisão: o personal confirma pela lista do Financeiro ("Pagamentos para confirmar", de todos os meses) ou pelo detalhe do demonstrativo. `Payment` vira `Confirmed` e o período `Paid` (com `PaidAtUtc`) na mesma transação.
- Consequências: o resumo Recebido/A receber do mês muda na hora. Auditado como `PaymentConfirmed`.
- Aprovado por: Vítor Mancini

### D-051 — "Não recebi"
- Data: 2026-11-10
- Estado: Aprovada
- Decisão: o personal recusa o aviso com mensagem opcional (até 500, visível ao aluno). `Payment` vira `Rejected` e o período volta para `Issued`; o aluno pode informar de novo.
- Consequências: nova coluna `Payment.DecisionNote` (`varchar(500)`, só com `Rejected`, `CK_Payment_DecisionNote`). O pagamento recusado fica no histórico. Auditado como `PaymentRejected`, sem a mensagem.
- Aprovado por: Vítor Mancini

### D-052 — Registro direto pelo personal
- Data: 2026-11-10
- Estado: Aprovada
- Contexto: pagamento em dinheiro ou Pix já visto, sem aviso do aluno.
- Decisão: o personal marca como pago um demonstrativo `Issued`, com uma confirmação simples. Gera `Payment` `Confirmed` e período `Paid`.
- Alternativas consideradas: coluna `Source`/`RecordedByPersonal` no `Payment`.
- Consequências: sem coluna nova: o registro direto tem `ReportedByUserId = DecidedByUserId =` personal e `ReportedAtUtc = DecidedAtUtc` (o aluno nunca decide, então a igualdade identifica o registro direto). Se o aluno informou enquanto isso, o personal é levado a confirmar o aviso. Auditado como `PaymentRecorded`.
- Aprovado por: Vítor Mancini

### D-053 — Histórico do pagamento
- Data: 2026-11-10
- Estado: Aprovada
- Decisão: o demonstrativo mostra a linha do tempo com emitido, informado(s), recusado(s) e confirmado (ou registrado), com data e hora. Confirmação é definitiva: não há desfazer.
- Consequências: depois de `Paid`, confirmar, recusar, registrar ou informar respondem "já está pago" sem alterar nada.
- Aprovado por: Vítor Mancini

### D-054 — Avisos de pagamento
- Data: 2026-11-10
- Estado: Aprovada
- Decisão: avisos por WhatsApp de pagamento informado, confirmado e recusado ficam para a Fase 6. Até lá o personal vê as pendências no Financeiro e no Início.
- Aprovado por: Vítor Mancini

### D-055 — Provedor de WhatsApp
- Data: 2026-11-12
- Estado: Aprovada
- Contexto: P-013; a Fase 6 precisa de um provedor para avisos e códigos.
- Decisão: Meta WhatsApp Cloud API direta, sem intermediário (BSP). Mensagens só por template aprovado, idioma `pt_BR`.
- Alternativas consideradas: provedores intermediários (Twilio, Zenvia, Gupshup).
- Consequências: adapter `MetaCloudWhatsAppSender` atrás de `IWhatsAppSender`; `LogWhatsAppSender` (padrão em Development) só registra no log. Os 9 templates de `WHATSAPP_TEMPLATES.md` precisam estar aprovados na Meta antes do uso. O provider `Log` não sobe em Production.
- Aprovado por: Vítor Mancini

### D-056 — Código de uso único por WhatsApp
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: 6 dígitos, válido por 10 minutos, no máximo 5 tentativas. Reenvio liberado 60 s depois do último envio; até 3 envios por hora por celular. Estourado um limite, o fluxo fica bloqueado por 15 minutos.
- Consequências: tabela `PhoneVerificationCode` com HMAC-SHA256 do código (chave `PhoneVerification:HashKey`), nunca o código. Interpretação registrada na implementação: o bloqueio pelo limite de envios começa no pedido do 4º código dentro da hora; o de tentativas, na 5ª errada. Novo código substitui o anterior. Envio e conferência serializados por celular (advisory lock) e pelo código (`FOR UPDATE`), então pedidos em paralelo não furam os limites.
- Aprovado por: Vítor Mancini

### D-057 — Aceite do convite com código (fecha D-033)
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: antes do formulário da conta, o aluno confirma um código enviado ao celular cadastrado pelo personal (`PersonalStudent.StudentPhone`). Abrir o link não envia nada: o aluno toca em "Enviar código pelo WhatsApp". Vale para conta nova e para quem já tem conta. Com sucesso, `AspNetUsers.PhoneNumber` recebe o celular e `PhoneNumberConfirmed = true`.
- Alternativas consideradas: manter o aceite só com o link; confirmar por e-mail.
- Consequências: a prova do código é um valor protegido pelo Data Protection, preso ao convite e ao celular, válido por 15 minutos, conferido de novo no POST do formulário. Trocar o celular do vínculo ou gerar novo convite invalida a prova. O personal não vê o código. O risco aceito de D-033 deixa de existir.
- Aprovado por: Vítor Mancini

### D-058 — Recuperação de senha por WhatsApp (fecha D-030)
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: link "Esqueci minha senha" no login. `/esqueci-senha` pede o e-mail e responde sempre igual. O código vai ao celular confirmado da conta (ou ao celular do perfil, no caso do personal). `/nova-senha` recebe o código e a nova senha (mesmas regras do convite). Com sucesso: token de troca gerado no servidor, `ResetPasswordAsync`, `UpdateSecurityStampAsync` e volta ao login com "Senha alterada. Entre com a nova senha.".
- Consequências: o e-mail vai de uma página para a outra num valor protegido (30 minutos), não em texto. O cookie confere o security stamp a cada minuto, então as outras sessões caem em até 1 minuto. O bloqueio de login por tentativas é zerado na troca. Quem não tem celular confirmado não recebe código (fala com o suporte).
- Aprovado por: Vítor Mancini

### D-059 — Alunos que aceitaram antes da Fase 6
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: sem celular confirmado, o aluno é levado a `/aluno/confirmar-celular` depois do login e só usa a área do aluno após confirmar, com o mesmo fluxo de código, no `StudentPhone` do vínculo mais recente.
- Consequências: o cookie de login carrega se o celular está confirmado (claim), então a exigência não consulta o banco a cada página. Conta que também é personal usa a área do personal normalmente.
- Aprovado por: Vítor Mancini

### D-060 — Sem texto livre no WhatsApp
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: motivos, observações e mensagens digitados por usuários nunca vão para o WhatsApp. A mensagem diz que há algo para ver e leva ao app.
- Consequências: variáveis dos templates são nomes, datas, horários, mês e valores; sem quebra de linha (sanitizadas). Nomes: o personal vê o aluno por `PersonalStudent.StudentName` (D-015); o aluno vê o personal por `PersonalProfile.DisplayName`.
- Aprovado por: Vítor Mancini

### D-061 — Horário de silêncio
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: avisos que sairiam entre 22:00 e 08:00 de São Paulo são agendados para 08:00. Códigos saem na hora.
- Consequências: vale também para as retentativas. Um demonstrativo emitido à 00:00 sai às 08:00.
- Aprovado por: Vítor Mancini

### D-062 — Botão para o app e retentativas
- Data: 2026-11-12
- Estado: Aprovada
- Contexto: P-014.
- Decisão: todo aviso tem um botão de URL para a tela certa (base `App:PublicBaseUrl`). Falhas passageiras são repetidas até 5 tentativas, com espera crescente (1 min, 5 min, 30 min, 2 h); depois o aviso fica `Failed` e é auditado. A ação (confirmar, pagar) acontece só dentro do app, com login.
- Consequências: o webhook da Meta (assinado) registra entregue/lido/falhou por mensagem, sem reenviar. Falha definitiva (número sem WhatsApp, template recusado) não é repetida. Avisos sem celular confirmado são encerrados como `sem-celular`.
- Aprovado por: Vítor Mancini

### D-063 — Envio do convite
- Data: 2026-11-12
- Estado: Aprovada
- Decisão: o link do convite continua indo pelo WhatsApp do próprio personal (wa.me), como em D-031. O PersonalHub só envia o código e os avisos.
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
