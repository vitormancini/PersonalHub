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

Se o proprietário insistir em manter segredo real em arquivo local, explicar o risco e garantir que o arquivo fique fora do controle de versão.

## 3. Senhas e autenticação

- Usar ASP.NET Core Identity ou solução madura equivalente.
- Senhas armazenadas somente como hash com algoritmo e parâmetros gerenciados pela biblioteca.
- Política de senha adequada e proteção contra tentativas automatizadas.
- Fluxo seguro de recuperação de senha.
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