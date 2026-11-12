# PersonalHub — Segurança e privacidade

## 1. Princípio

O sistema processa dados pessoais e informações de relacionamento comercial. Segurança, privacidade e LGPD devem ser consideradas desde a primeira entrega, mesmo no MVP.

## 2. Segredos e configuração

O requisito inicial menciona manter dados sensíveis no `appsettings.json` durante o MVP. Isso não deve significar versionar segredos reais.

Política:
- `appsettings.json`: somente configuração não secreta.
- Desenvolvimento local: .NET User Secrets ou variáveis de ambiente.
- CI/CD e produção: secret manager/variáveis seguras do ambiente.
- `appsettings.example.json`: placeholders fictícios.
- Adicionar `.env`, arquivos de secrets e configurações locais ao `.gitignore`.
- Nunca commitar senha de banco, chave de API, token WhatsApp, chave de assinatura ou credencial AWS.
- Segredos da Fase 6 (só user-secrets ou variáveis de ambiente): `WhatsApp:AccessToken`, `WhatsApp:AppSecret`, `WhatsApp:WebhookVerifyToken` e `PhoneVerification:HashKey`. Fora de Development, o app não sobe sem `PhoneVerification:HashKey`, nem com `WhatsApp:Provider = Meta` sem token e id do número; `Provider = Log` é recusado em Production (o log mostraria os códigos). Nenhuma mensagem de erro de configuração repete o valor de um segredo.
- Chaves do Data Protection: protegem o cookie, o antiforgery, o código que espera na outbox e as provas do convite e da troca de senha. Na AWS, precisam ser persistidas e compartilhadas entre instâncias (decisão de hospedagem pendente); se forem perdidas, os códigos pendentes deixam de valer (o aviso fica `Failed` com `codigo-ilegivel`) e os usuários precisam entrar de novo.

Se o proprietário insistir em manter segredo real em arquivo local, explicar o risco e garantir que o arquivo fique fora do controle de versão.

## 3. Senhas e autenticação

- Usar ASP.NET Core Identity ou solução madura equivalente.
- Senhas armazenadas somente como hash com algoritmo e parâmetros gerenciados pela biblioteca.
- Política de senha adequada e proteção contra tentativas automatizadas.
- Fluxo seguro de recuperação de senha (D-058): código por WhatsApp no celular confirmado; resposta igual exista ou não a conta; token do Identity gerado e usado no servidor; security stamp trocado (o cookie confere o stamp a cada 1 minuto, então as outras sessões caem); bloqueio de login zerado. Risco residual: a tela do código diz "código incorreto" quando um código foi enviado e "expirou" quando não foi; isso só revela algo a quem já disparou um WhatsApp para o dono do e-mail, com limite de 3 por hora.
- Cookies seguros: HttpOnly, Secure em HTTPS, SameSite adequado.
- Encerrar/inutilizar sessões quando necessário.
- Não criar autenticação caseira.
- MFA pode ser avaliado posteriormente, mas registrar riscos e decisão.

## 4. Autorização e isolamento de dados

A autorização deve ser aplicada no servidor em cada operação, não apenas ocultando botões.

- Personal só acessa alunos vinculados a ele.
- Personal só registra/exclui aulas de seus próprios vínculos.
- Aluno só consulta aulas, demonstrativos e pagamentos próprios.
- Aluno só responde aulas associadas ao seu vínculo.
- Personal só confirma pagamento de período pertencente ao seu vínculo.
- IDs imprevisíveis não substituem autorização.
- Testar tentativas de IDOR/BOLA alterando IDs na URL ou payload.
- Operações de escrita devem validar o usuário autenticado e o vínculo no banco.

## 5. Dados pessoais e LGPD

- Coletar apenas dados necessários para o serviço.
- Confirmar finalidade e necessidade de CPF antes de torná-lo obrigatório.
- Definir base legal, transparência, retenção e processo de atendimento aos direitos do titular com orientação apropriada.
- Proteger dados pessoais em trânsito (HTTPS) e em repouso conforme infraestrutura.
- Restringir acesso operacional ao banco.
- Não usar dados reais em ambientes de desenvolvimento/teste.
- Definir política para correção, exportação, anonimização e exclusão quando aplicável.
- Avaliar contratos e responsabilidades com provedor de WhatsApp e hospedagem.

## 6. CPF e informações sensíveis

Se CPF for necessário:
- armazenar de forma protegida;
- limitar exibição (mascarar quando possível);
- evitar logs, URLs, analytics e mensagens WhatsApp;
- controlar acesso;
- definir índice/estratégia de unicidade sem expor o valor;
- avaliar criptografia em nível de aplicação ou serviço gerenciado, incluindo gestão e rotação de chaves.

Não assumir que CPF precisa ser coletado apenas porque foi citado como exemplo.

## 7. Blazor Server / Interactive Server

- Proteger circuitos e cookies.
- Validar autorização em handlers e operações, não apenas no componente.
- Não confiar em estado mantido no cliente.
- Considerar expiração de sessão, desconexão e reconexão.
- Não expor segredos em parâmetros, HTML ou JavaScript.
- Proteger operações sensíveis contra CSRF conforme mecanismos do framework.
- Aplicar limites de tamanho e validação em entradas.

## 8. WhatsApp e links de ação

- Exigir opt-in e respeitar políticas vigentes do provedor/plataforma.
- Não colocar CPF, valores detalhados ou dados sensíveis em URLs.
- Links de aprovação devem usar token aleatório, de uso limitado, com expiração, propósito específico e armazenamento seguro (hash do token quando viável).
- Preferir que o link leve a uma página que autentique ou confirme a identidade antes de executar ação crítica.
- Não permitir que um simples GET altere estado: scanners de link podem abrir URLs automaticamente.
- Usar confirmação explícita por POST/ação do usuário.
- Validar assinatura/autenticidade dos webhooks e proteger contra replay.
- Rate limit e registrar tentativas suspeitas.
- ~~Risco aceito no MVP (D-033)~~ **Fechado na Fase 6 (D-057):** o aceite do convite exige o código enviado por WhatsApp ao celular cadastrado pelo personal, que não vê o código. A prova do código é um valor protegido (Data Protection) preso ao convite e ao celular, válido por 15 minutos e conferido de novo no POST da conta. Sem ela, nem a senha da conta existente é testada. Alunos de antes da Fase 6 confirmam o celular depois do login (D-059). Risco que permanece: o personal cadastra o celular; se cadastrar o próprio número, recebe o código. O texto "Esse não é o seu número?" orienta o aluno, e a troca de celular pelo personal invalida provas em andamento.

### Códigos por WhatsApp (D-056)
- 6 dígitos do gerador criptográfico; 10 minutos; 5 tentativas; reenvio após 60 s; 3 envios por hora por celular; bloqueio de 15 minutos.
- No banco, só o HMAC-SHA256 do código com chave do servidor (`PhoneVerification:HashKey`); com 1 milhão de códigos possíveis, um hash simples seria revertido por quem lesse o banco. A comparação é em tempo constante.
- Na outbox, o código vai protegido pelo Data Protection (finalidade própria) e o payload é apagado quando a mensagem sai ou falha de vez.
- Envio e conferência serializados (advisory lock por celular e `FOR UPDATE` no código): pedidos em paralelo não furam os limites.
- Páginas e ações de código têm rate limit por IP (`PhoneVerification:RateLimit`), além dos limites por celular.
- Auditoria: `PhoneCodeSent`, `PhoneCodeVerified`, `PhoneCodeFailed`, `PhoneCodeLocked`, `PhoneConfirmed`, `PasswordReset`, `NotificationFailed`, `NotificationDeliveryFailed`, só com finalidade e contagens (nunca celular, e-mail ou código).

### Webhook da Meta (D-062)
- `/webhooks/whatsapp`, público e sem antiforgery (não há cookie nem formulário), com rate limit por IP (`WhatsApp:WebhookRateLimit`) e corpo limitado a 256 KB.
- Não existe (404) sem `WhatsApp:AppSecret`.
- GET de verificação compara `hub.verify_token` em tempo constante.
- POST exige `X-Hub-Signature-256` = HMAC-SHA256 do corpo exato com o AppSecret, comparado em tempo constante; sem assinatura válida, 401 e nada muda.
- Replay: o webhook só registra entrega/leitura e a situação só avança, então repetir um evento não altera nada. Nenhuma ação de negócio é disparada pelo webhook.

### Conteúdo das mensagens (D-060)
- Nenhum texto livre digitado por usuário (motivos, observações, mensagens) vai para o WhatsApp. Variáveis: nomes, datas, horários, mês e valores, sem quebra de linha.
- Os botões levam ao app, que exige login e confere a propriedade de cada recurso; o link não carrega token nem executa ação.
- Logs do adapter: template e status/código de erro; telefone sempre mascarado (só os 4 últimos dígitos); nunca o corpo de resposta da Meta, o token ou o código (exceto no provider `Log`, só fora de Production).

## 9. Auditoria

Registrar ações críticas:
- registro, reconhecimento, recusa e exclusão de aula;
- alteração de dados comerciais relevantes;
- fechamento de demonstrativo;
- declaração e confirmação de pagamento;
- alterações de vínculo e permissões.

Auditoria deve registrar ator, ação, entidade, horário e correlation ID, sem copiar dados sensíveis desnecessários.

## 10. Proteções gerais

- Validação server-side.
- Encoding de saída e prevenção de XSS.
- Queries parametrizadas/EF Core.
- Proteção contra CSRF.
- Rate limiting em endpoints sensíveis.
- Headers de segurança e HTTPS.
- Dependências atualizadas e verificadas.
- Tratamento de erros sem stack trace para usuário final.
- Backup, restore testado e política de acesso mínimo.
- Revisão de configuração antes de produção.

## 11. Checklist de segurança por feature

- [ ] Quem pode executar a ação?
- [ ] O servidor verifica propriedade do recurso?
- [ ] Entradas são validadas?
- [ ] Há risco de repetição/concorrência?
- [ ] Dados pessoais são minimizados?
- [ ] Logs estão sanitizados?
- [ ] Notificações e URLs não vazam informações?
- [ ] Há testes negativos de autorização?
- [ ] Falhas externas não deixam dados inconsistentes?