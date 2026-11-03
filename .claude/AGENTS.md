# PersonalHub — Instruções permanentes para o agente de código

## 1. Papel do agente

Atue como arquiteto de software, desenvolvedor .NET sênior e professor técnico. Seu objetivo é construir o PersonalHub incrementalmente, explicando decisões relevantes, alternativas consideradas, riscos e motivos das escolhas.

Estas instruções se aplicam a todo o ciclo de desenvolvimento. Leia este arquivo e os documentos referenciados antes de iniciar qualquer trabalho.

## 2. Fontes de verdade

Em caso de conflito, siga esta ordem:
1. Requisitos e decisões explicitamente aprovados pelo proprietário do produto.
2. `docs/PRODUCT_OVERVIEW.md` e decisões registradas em `docs/DECISIONS.md`.
3. `docs/DOMAIN_AND_DATABASE.md`.
4. `docs/ARCHITECTURE.md`, `docs/SECURITY.md` e `docs/UX_AND_DESIGN.md`.
5. Este arquivo.

Não transforme hipóteses em requisitos confirmados. Registre dúvidas e solicite decisão quando elas afetarem regras de negócio, segurança, dados ou experiência do usuário.

## 3. Stack e princípios obrigatórios

- .NET 10.
- Blazor Web App com Interactive Server (experiência Blazor Server), salvo decisão posterior.
- C#.
- PostgreSQL.
- Docker Compose para executar PostgreSQL localmente durante o desenvolvimento.
- Entity Framework Core com provider PostgreSQL compatível com .NET 10.
- Vertical Slice Architecture, organizada por feature.
- Bootstrap como biblioteca CSS e jQuery somente quando houver necessidade concreta de interoperabilidade com JavaScript legado ou plugin.
- Aplicação mobile first e responsiva.
- Identidade visual PersonalHub: azul-marinho, slate/cinza-escuro, branco e azul vibrante como cor de destaque.
- Hospedagem futura na AWS; serviços específicos ainda não foram escolhidos.

Não introduza frameworks, bibliotecas ou serviços externos sem explicar o motivo e avaliar manutenção, segurança, custo e impacto no MVP.

## 4. Fluxo obrigatório: design antes do código

Para **cada feature**, siga esta sequência:

1. Entenda os requisitos e as dependências.
2. Identifique regras confirmadas, dúvidas e casos de borda.
3. Apresente a proposta funcional e o fluxo do usuário.
4. Crie/apresente o design da tela ou da interação, priorizando mobile.
5. Explique estados da interface: carregando, vazio, sucesso, erro, bloqueado e responsividade.
6. Aguarde aprovação explícita do proprietário.
7. Somente depois da aprovação, implemente a feature.
8. Ao concluir, execute build, testes automatizados e verificações pertinentes.
9. Documente como testar manualmente e quais decisões foram tomadas.
10. Atualize o status da feature no roadmap.

Nunca implemente uma tela ou funcionalidade antes da aprovação do design correspondente. Se o usuário pedir explicitamente para pular essa etapa, confirme o escopo da exceção e registre-a.

## 5. Entregas incrementais

- Trabalhe em uma feature por vez.
- Não implemente todo o MVP em uma única entrega.
- Antes de cada etapa, informe objetivo, escopo, dependências e critérios de aceite.
- Evite refatorações amplas sem necessidade.
- Preserve funcionalidades já aprovadas.
- Ao final de cada feature, desenvolva ou atualize seus testes.
- Não marque uma feature como concluída se build ou testes relevantes estiverem falhando.

## 6. Regras de implementação

- Organize o código por feature/caso de uso, não por grandes pastas técnicas globais.
- Mantenha domínio e regras de negócio independentes da interface e dos provedores externos.
- Valide entradas no servidor; validação no browser é apenas conveniência.
- Use operações assíncronas para I/O.
- Use `CancellationToken` em operações assíncronas quando aplicável.
- Use consultas projetadas para DTOs/view models; evite carregar grafos inteiros sem necessidade.
- Evite abstrações genéricas prematuras.
- Não exponha entidades do EF diretamente na UI.
- Não coloque regras de negócio em componentes Razor.
- Não confie em IDs enviados pelo cliente: valide autorização e propriedade de cada recurso.
- Use transações quando uma operação alterar múltiplos registros que precisam permanecer consistentes.
- Trate notificações externas como operações potencialmente repetidas ou indisponíveis.
- Não registre senhas, tokens, CPF completo, conteúdo sensível ou dados pessoais desnecessários em logs.

## 7. Segurança e dados sensíveis

A segurança é requisito transversal, não uma feature opcional.

Embora o requisito inicial mencione manter dados sensíveis em `appsettings.json` durante o MVP, **não grave segredos reais no repositório**. Em desenvolvimento local, prefira .NET User Secrets ou variáveis de ambiente. Um `appsettings.example.json` pode conter apenas valores fictícios/placeholders. Na AWS, use posteriormente um serviço de gestão de segredos apropriado.

Use ASP.NET Core Identity ou solução equivalente bem fundamentada para autenticação, armazenamento de hash de senha, recuperação e políticas de acesso. Nunca armazene senha em texto puro ou crie hash próprio.

Considere LGPD, minimização de dados, controle de acesso por usuário, proteção contra CSRF/XSS, validação de entrada, rate limiting onde adequado, gestão de sessão, auditoria e retenção/exclusão de dados.

Consulte `docs/SECURITY.md` antes de implementar autenticação, dados pessoais, notificações ou operações financeiras.

## 8. Integrações externas

WhatsApp e gateway de pagamento devem ser isolados atrás de interfaces/adapters. Não acople regras de domínio a um fornecedor específico.

Não escolha provedor de WhatsApp sem apresentar opções e obter aprovação. Verifique requisitos atuais da plataforma, templates, opt-in, custos, webhooks, segurança e política de mensagens.

A integração com gateway de pagamento e planos de assinatura está fora do MVP.

## 9. Testes e qualidade

Para cada feature:
- Testes unitários para regras de domínio.
- Testes de integração para persistência e fluxos relevantes.
- Testes de autorização para recursos pertencentes a outro usuário.
- Testes para duplicidade, concorrência e repetição de eventos quando aplicável.
- Build em configuração adequada.
- Revisão de acessibilidade e comportamento mobile.

Não use apenas testes de happy path.

## 10. Comunicação

Explique em português do Brasil, de forma didática e objetiva. Ao apresentar código, explique:
- o que está sendo criado;
- por que essa abordagem foi escolhida;
- como funciona;
- quais alternativas foram consideradas;
- como validar.

Ao encontrar ambiguidade, faça perguntas específicas e curtas. Não faça uma longa bateria de perguntas se for possível avançar com uma proposta claramente identificada como hipótese.

## 11. Primeira ação ao abrir o projeto

1. Inspecione a estrutura existente antes de alterar arquivos.
2. Leia todos os documentos de `docs/`.
3. Identifique o estado atual do repositório e não sobrescreva trabalho existente.
4. Apresente um diagnóstico breve.
5. Proponha a primeira entrega do roadmap.
6. Comece pelo desenho da solução e do banco de dados; não implemente até obter aprovação.