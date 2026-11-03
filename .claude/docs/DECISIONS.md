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

## Pendências que exigem decisão

| ID | Tema | Pergunta |
|---|---|---|
| P-001 | Relação personal/aluno | Um aluno pode ter mais de um personal? |
| P-002 | Conta do aluno | Conta criada no cadastro ou convite posterior? |
| P-003 | Dados pessoais | CPF é obrigatório? Quais campos são realmente necessários? |
| P-004 | Preço | Cobrança por aula, mensalidade fixa ou opção configurável? |
| P-005 | Período | Mês calendário ou ciclo entre vencimentos? |
| P-006 | Aula cobrável | Aula pendente entra no demonstrativo ou somente aprovada? |
| P-007 | Recusa | É obrigatório informar motivo? Como resolver aula recusada? |
| P-008 | Prazo | Existe prazo para aluno reconhecer/recusar? |
| P-009 | Exclusão | Como ajustar período já fechado se aula for excluída? |
| P-010 | Pagamento | Permitir pagamento parcial ou múltiplos pagamentos? |
| P-011 | Vencimento | Regra para dias 29, 30 e 31 em meses curtos? |
| P-012 | Fuso | Confirmar `America/Sao_Paulo` como fuso do MVP. |
| P-013 | WhatsApp | Qual provedor será utilizado? |
| P-014 | WhatsApp | Como validar reconhecimento via WhatsApp: link autenticado, resposta ou ambos? |
| P-015 | Convites | Como o aluno recebe credenciais/acesso inicial? |
| P-016 | Admin | Provisionamento de personal por SQL/seed/script ou ferramenta administrativa mínima? |
| P-017 | Exclusão | Aluno e personal podem encerrar vínculo? Qual política de retenção? |
| P-018 | Notificação | Quais eventos também terão notificação dentro da aplicação? |

## Modelo para novas decisões

### D-XXX — Título
- Data:
- Estado: Proposta / Aprovada / Substituída
- Contexto:
- Decisão:
- Alternativas consideradas:
- Consequências:
- Aprovado por: