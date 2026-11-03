# PersonalHub — UX, identidade visual e design

## 1. Diretriz de experiência

A aplicação é mobile first. A maioria dos acessos será feita por celular, especialmente para registrar aulas, responder solicitações e consultar demonstrativos.

As telas devem ser simples, rápidas e legíveis. Priorizar ações frequentes e reduzir quantidade de etapas.

## 2. Identidade visual

Nome: **PersonalHub**.

Paleta de referência:
- Azul-marinho: cor principal para cabeçalho, navegação e áreas de destaque.
- Slate/cinza-escuro: superfícies secundárias, textos e elementos estruturais.
- Azul vibrante: cor de ação primária, links e destaques.
- Branco/cinza muito claro: fundo de conteúdo e cartões.
- Cores semânticas: verde para sucesso/aprovado/pago; amarelo/âmbar para pendente; vermelho para recusado/erro; sempre acompanhadas de texto/ícone.

Sugestão inicial de tokens (ajustar após aprovação visual):
- `--ph-navy: #102A43`
- `--ph-navy-deep: #0B1F33`
- `--ph-slate: #334155`
- `--ph-slate-muted: #64748B`
- `--ph-blue: #0D8FE8`
- `--ph-blue-light: #E6F4FF`
- `--ph-surface: #FFFFFF`
- `--ph-background: #F4F7FB`

Esses valores são ponto de partida, não identidade final imutável.

## 3. Referências existentes

As imagens de referência fornecidas pelo proprietário ficam na pasta `examples/` do projeto. Usá-las como inspiração de linguagem visual, não como telas rígidas para copiar.

Elementos de referência:
- Logo PersonalHub com símbolo associado a movimento/fitness e wordmark.
- Cabeçalho azul-marinho.
- Azul vibrante para CTAs e ícones.
- Cards claros com cantos arredondados.
- Tipografia sem serifa e hierarquia forte.
- Dashboard com resumo de aulas, faturamento e atalhos.
- Uso de imagens de academia em áreas de apresentação, sem comprometer legibilidade.

Não presumir que imagens anteriores estão fisicamente disponíveis; inspecionar `examples/` e solicitar arquivos se necessário.

## 4. Bootstrap

Usar Bootstrap como base:
- Container/grid e breakpoints.
- Forms, buttons, cards, navbar, badges, alerts e modals.
- Utilitários responsivos.
- Componentes customizados via CSS com prefixo `ph-` para evitar conflitos.
- Não criar um design system excessivamente complexo no MVP.

A interface deve continuar consistente em desktop, tablet e celular.

## 5. Navegação do personal

Proposta inicial para mobile:
- Início
- Aulas
- Alunos
- Financeiro

Ações em destaque no início:
- Consultar aulas registradas.
- Consultar alunos.
- Registrar aula.
- Aprovações pendentes.
- Faturamento do mês.

A navegação final deve ser validada por protótipo antes da implementação.

## 6. Navegação do aluno

Proposta inicial:
- Início/resumo do mês.
- Minhas aulas.
- Demonstrativo/pagamentos.
- Perfil/ajuda.

Ações principais:
- Reconhecer ou recusar aula pendente.
- Consultar histórico.
- Consultar valor e período de cobrança.
- Informar pagamento realizado.

## 7. Estados de interface obrigatórios

Para cada tela, desenhar e implementar quando aplicável:
- Carregamento.
- Conteúdo normal.
- Lista vazia.
- Erro de comunicação.
- Sem permissão.
- Estado pendente.
- Sucesso após ação.
- Falha de validação.
- Desconexão/reconexão quando relevante no Interactive Server.

Status não deve depender somente de cor. Usar texto e ícone.

## 8. Acessibilidade e usabilidade

- Contraste adequado.
- Tamanho de toque confortável (alvo aproximado de 44px).
- Labels claros em formulários.
- Mensagens de validação próximas ao campo.
- Foco visível e navegação por teclado.
- Não usar somente ícones sem rótulo para ações pouco óbvias.
- Evitar tabelas largas em celular; preferir cards/listas responsivas.
- Confirmar ações destrutivas e explicar consequências.
- Exibir data, período e valores em formato brasileiro, com moeda BRL.

## 9. Processo de aprovação por feature

Antes do código, apresentar:
1. Objetivo e usuário da tela.
2. Fluxo de navegação.
3. Wireframe ou mockup visual.
4. Conteúdo e ações de cada componente.
5. Estados de interface.
6. Comportamento mobile e desktop.
7. Regras de validação e acessibilidade.
8. Dúvidas e decisões necessárias.

Aguardar aprovação explícita. Após aprovação, implementar o layout com Bootstrap e os tokens PersonalHub.