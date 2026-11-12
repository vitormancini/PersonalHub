using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Notifications;
using PersonalHub.IntegrationTests.Observability;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.AcceptInvite;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Observability;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class AcceptInviteTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string NewPassword = "Aluna-Forte1";

    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;
    private TestAccount _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString, logs: _logs);
        _client = _factory.CreateBrowser();
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Dictionary<string, string> NewAccountFields(
        string name = "Mariana L.", string password = NewPassword, string? confirm = null, bool terms = true)
    {
        var fields = new Dictionary<string, string>
        {
            ["New.Name"] = name,
            ["New.Password"] = password,
            ["New.ConfirmPassword"] = confirm ?? password
        };
        if (terms)
        {
            fields["New.AcceptTerms"] = "true";
        }

        return fields;
    }

    private static Dictionary<string, string> ExistingAccountFields(string password, bool terms = true)
    {
        var fields = new Dictionary<string, string> { ["Existing.Password"] = password };
        if (terms)
        {
            fields["Existing.AcceptTerms"] = "true";
        }

        return fields;
    }

    /// <summary>Confirma o código do WhatsApp (D-057) e envia o formulário da conta nova com a prova.</summary>
    private async Task<HttpResponseMessage> AcceptNewAsync(HttpClient client, string token, Dictionary<string, string> fields)
    {
        fields["New.PhoneProof"] = await ProofAsync(client, token);
        return await client.PostFormAsync($"/convite/{token}", "nova-conta", fields);
    }

    private async Task<HttpResponseMessage> AcceptExistingAsync(HttpClient client, string token, Dictionary<string, string> fields)
    {
        fields["Existing.PhoneProof"] = await ProofAsync(client, token);
        return await client.PostFormAsync($"/convite/{token}", "conta-existente", fields);
    }

    // Uma prova por cliente e convite: vale 15 minutos para os reenvios do formulário (como no browser).
    private readonly Dictionary<(HttpClient, string), string> _proofs = new();

    private async Task<string> ProofAsync(HttpClient client, string token)
    {
        if (!_proofs.TryGetValue((client, token), out var proof))
        {
            proof = await client.VerifyInvitePhoneAsync(_factory, token);
            _proofs[(client, token)] = proof;
        }

        return proof;
    }

    [Fact]
    public async Task Page_ForValidInvite_ShowsPersonalEmailAndPrefilledName_WithNoReferrer()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await _client.GetAsync($"/convite/{invite.Token}");
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Personal Teste convidou você", html);
        Assert.Contains("Primeiro, confirme que este WhatsApp é seu.", html);
        Assert.Contains("WhatsApp cadastrado pelo Personal", html);
        Assert.Contains("Enviar código pelo WhatsApp", html);
        // Antes do código, nada do formulário da conta (nem o e-mail).
        Assert.DoesNotContain(invite.Email, html);
        Assert.DoesNotContain("Criar conta e entrar", html);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task AfterCode_ShowsAccountFormWithEmailAndPrefilledName()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        await _client.PostFormAsync($"/convite/{invite.Token}", "enviar-codigo", new Dictionary<string, string>());
        var code = await _factory.LatestCodeAsync(Web.Domain.PhoneVerification.PhoneCodePurpose.InviteAcceptance,
            invite.PersonalStudentId);
        var html = await (await _client.PostFormAsync($"/convite/{invite.Token}", "confirmar-codigo",
            new Dictionary<string, string> { ["Code.Value"] = code })).TextAsync();

        Assert.Contains("Sua conta será criada com o e-mail", html);
        Assert.Contains(invite.Email, html);
        Assert.Contains("value=\"Mariana Lopes\"", html);
        Assert.Contains("Criar conta e entrar", html);
        Assert.Contains("href=\"/termos\"", html);
        Assert.Contains("href=\"/privacidade\"", html);
        Assert.NotNull(NotificationTestHelpers.ProofIn(html));
    }

    [Fact]
    public async Task AcceptNew_CreatesStudentAccount_ActivatesLink_UsesInvite_AndSignsIn()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await AcceptNewAsync(_client, invite.Token, NewAccountFields(name: "  Mariana   Lopes "));

        Assert.Equal("/aluno", response.RedirectTarget());
        Assert.NotNull(AuthTestHelpers.AuthCookieHeader(response));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/aluno")).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<Web.Infrastructure.Persistence.AppDbContext>();
        var user = await users.FindByEmailAsync(invite.Email);
        Assert.NotNull(user);
        Assert.Equal("Mariana Lopes", user.FullName);
        Assert.True(await users.CheckPasswordAsync(user, NewPassword));
        Assert.Equal([AppRoles.Student], await users.GetRolesAsync(user));

        var link = await db.PersonalStudents.SingleAsync(s => s.Id == invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Active, link.Status);
        Assert.Equal(user.Id, link.StudentUserId);
        Assert.NotNull(link.ActivatedAtUtc);
        // O nome cadastrado pelo personal no vínculo não muda.
        Assert.Equal("Mariana Lopes", link.StudentName);

        var storedInvite = await db.StudentInvites.SingleAsync(i => i.PersonalStudentId == invite.PersonalStudentId);
        Assert.NotNull(storedInvite.UsedAtUtc);

        var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == invite.PersonalStudentId && a.Action == "InviteAccepted");
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Contains("\"termsAccepted\":true", audit.Metadata!.Replace(" ", ""));
        Assert.DoesNotContain(invite.Email, audit.Metadata);
    }

    [Theory]
    [InlineData("curta1A", null, true, "A senha precisa ter pelo menos 8 caracteres.")]
    [InlineData("semnumeroA", null, true, "A senha precisa ter pelo menos um número.")]
    [InlineData("SEMMINUSCULA1", null, true, "A senha precisa ter pelo menos uma letra minúscula.")]
    [InlineData("semmaiuscula1", null, true, "A senha precisa ter pelo menos uma letra maiúscula.")]
    [InlineData(NewPassword, "Outra-Senha1", true, "As senhas não são iguais.")]
    [InlineData(NewPassword, null, false, "Aceite os Termos de Uso e a Política de Privacidade para continuar.")]
    public async Task AcceptNew_WithInvalidInput_ShowsError_AndChangesNothing(
        string password, string? confirm, bool terms, string message)
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await AcceptNewAsync(_client, invite.Token, NewAccountFields(password: password, confirm: confirm, terms: terms));
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html);
        Assert.DoesNotContain(password, html);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task AcceptNew_WithEmptyName_ShowsError()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await AcceptNewAsync(_client, invite.Token, NewAccountFields(name: "   "));

        Assert.Contains("Informe seu nome.", await response.TextAsync());
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task ExistingAccount_SeesLoginForm_AndAcceptsWithRightPassword()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email.ToUpperInvariant());

        // O formulário de login aparece depois do código do WhatsApp (D-057).
        await _client.PostFormAsync($"/convite/{invite.Token}", "enviar-codigo", new Dictionary<string, string>());
        var code = await _factory.LatestCodeAsync(Web.Domain.PhoneVerification.PhoneCodePurpose.InviteAcceptance,
            invite.PersonalStudentId);
        var verified = await _client.PostFormAsync($"/convite/{invite.Token}", "confirmar-codigo",
            new Dictionary<string, string> { ["Code.Value"] = code });
        var page = await verified.TextAsync();
        _proofs[(_client, invite.Token)] = NotificationTestHelpers.ProofIn(await verified.Content.ReadAsStringAsync())!;
        Assert.Contains("Você já tem conta no PersonalHub. Entre com sua senha para aceitar o convite do Personal Teste.", page);
        Assert.Contains("Entrar e aceitar convite", page);
        Assert.DoesNotContain("Crie uma senha", page);

        var response = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields(student.Password));

        Assert.Equal("/aluno", response.RedirectTarget());
        var link = await _factory.WithDbAsync(db => db.PersonalStudents.SingleAsync(s => s.Id == invite.PersonalStudentId));
        Assert.Equal(student.Id, link.StudentUserId);
        Assert.Equal(PersonalStudentStatus.Active, link.Status);
    }

    [Fact]
    public async Task ExistingAccount_WithWrongPassword_ShowsGenericError_AndChangesNothing()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email);

        var response = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields("Errada-123"));
        var html = await response.TextAsync();

        Assert.Contains("E-mail ou senha incorretos.", html);
        Assert.DoesNotContain("Errada-123", html);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task ExistingAccount_LocksAfterFiveWrongPasswords()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields("Errada-123"));
        }

        var fifth = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields("Errada-123"));
        Assert.Contains("Acesso bloqueado por 15 minutos", await fifth.TextAsync());

        var correct = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields(student.Password));
        Assert.Contains("Acesso bloqueado por 15 minutos", await correct.TextAsync());
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task ExistingAccount_WithoutTerms_IsRejected()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email);

        var response = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields(student.Password, terms: false));

        Assert.Contains("Aceite os Termos de Uso e a Política de Privacidade para continuar.", await response.TextAsync());
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task SecondPersonal_LinksTheSameAccount_ToBothPersonals()
    {
        var email = $"dois-personais-{Guid.NewGuid():N}@example.test";
        var first = await _factory.CreateInviteAsync(_personal.Id, email);
        await AcceptNewAsync(_client, first.Token, NewAccountFields());

        var otherPersonal = await _factory.CreateAccountAsync(AppRoles.Personal);
        var second = await _factory.CreateInviteAsync(otherPersonal.Id, email, name: "Mari");
        var freshClient = _factory.CreateBrowser();
        var response = await AcceptExistingAsync(freshClient, second.Token, ExistingAccountFields(NewPassword));

        Assert.Equal("/aluno", response.RedirectTarget());
        var links = await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.StudentEmail == email).ToListAsync());
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal(PersonalStudentStatus.Active, l.Status));
        Assert.Single(links.Select(l => l.StudentUserId).Distinct());
        Assert.Equal(2, links.Select(l => l.PersonalId).Distinct().Count());
        Assert.Equal(1, await _factory.WithDbAsync(db => db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant())));
    }

    [Fact]
    public async Task PersonalAccount_CanAlsoBeAStudentOfAnotherPersonal()
    {
        var otherPersonal = await _factory.CreateAccountAsync(AppRoles.Personal);
        var invite = await _factory.CreateInviteAsync(_personal.Id, otherPersonal.Email);

        var response = await AcceptExistingAsync(_client, invite.Token, ExistingAccountFields(otherPersonal.Password));

        Assert.Equal("/aluno", response.RedirectTarget());
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = await users.GetRolesAsync((await users.FindByIdAsync(otherPersonal.Id.ToString()))!);
        Assert.Contains(AppRoles.Personal, roles);
        Assert.Contains(AppRoles.Student, roles);
    }

    [Fact]
    public async Task UsedInvite_ShowsUsedPage_AndCannotBeAcceptedAgain()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await AcceptNewAsync(_client, invite.Token, NewAccountFields());

        var fresh = _factory.CreateBrowser();
        var page = await (await fresh.GetAsync($"/convite/{invite.Token}")).TextAsync();

        Assert.Contains("Este convite já foi usado", page);
        Assert.Contains("Sua conta já está criada. Entre com seu e-mail e senha.", page);
        Assert.Contains("Ir para o login", page);
        Assert.DoesNotContain(invite.Email, page);
    }

    [Fact]
    public async Task ExpiredInvite_ShowsExpiredPage_AndPostDoesNothing()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var pageBefore = await _client.GetAsync($"/convite/{invite.Token}");
        var antiforgery = AuthTestHelpers.AntiforgeryToken(await pageBefore.Content.ReadAsStringAsync());
        await _factory.SetInviteAsync(invite.PersonalStudentId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var page = await (await _client.GetAsync($"/convite/{invite.Token}")).TextAsync();
        Assert.Contains("Este convite expirou", page);
        Assert.Contains("Convites valem por 7 dias. Peça ao seu personal para enviar um novo link.", page);
        Assert.DoesNotContain(invite.Email, page);

        // Formulário aberto antes de expirar e enviado depois.
        var form = NewAccountFields();
        form["_handler"] = "nova-conta";
        form["__RequestVerificationToken"] = antiforgery;
        var post = await _client.PostAsync($"/convite/{invite.Token}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("Este convite expirou", await post.TextAsync());
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task RevokedInvite_ShowsNotFound()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await _factory.SetInviteAsync(invite.PersonalStudentId, revokedAtUtc: DateTime.UtcNow);

        var page = await (await _client.GetAsync($"/convite/{invite.Token}")).TextAsync();

        Assert.Contains("Convite não encontrado", page);
        Assert.Contains("Confira se o link está completo ou peça ao seu personal para enviar um novo.", page);
    }

    [Fact]
    public async Task InactiveLink_ShowsNotFound()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == invite.PersonalStudentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PersonalStudentStatus.Inactive)
                .SetProperty(x => x.EndedAtUtc, DateTime.UtcNow)));

        var page = await (await _client.GetAsync($"/convite/{invite.Token}")).TextAsync();

        Assert.Contains("Convite não encontrado", page);
        Assert.DoesNotContain(invite.Email, page);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // formato válido, token inexistente
    [InlineData("%27%20OR%201%3D1--")]
    public async Task UnknownOrMalformedToken_ShowsNotFound(string token)
    {
        var response = await _client.GetAsync($"/convite/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Convite não encontrado", await response.TextAsync());
    }

    [Fact]
    public async Task ParallelAccepts_OnlyOneWins()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var proof = await _factory.InviteProofAsync(invite.Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async i =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            // O SignInManager precisa de um HttpContext para gravar o cookie.
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
                new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var handler = scope.ServiceProvider.GetRequiredService<AcceptInviteHandler>();
            return await handler.AcceptWithNewAccountAsync(invite.Token, new NewAccountInput
            {
                Name = $"Tentativa {i}",
                Password = NewPassword,
                ConfirmPassword = NewPassword,
                AcceptTerms = true,
                PhoneProof = proof
            }, null, CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == AcceptInviteOutcome.Succeeded);
        Assert.All(results.Where(r => r.Outcome != AcceptInviteOutcome.Succeeded), r => Assert.True(
            r.Outcome == AcceptInviteOutcome.AccountExists
            || (r.Outcome == AcceptInviteOutcome.InviteUnavailable && r.State == InviteState.Used),
            $"Resultado inesperado: {r.Outcome}/{r.State}"));

        Assert.Equal(1, await _factory.WithDbAsync(db => db.Users.CountAsync(u => u.NormalizedEmail == invite.Email.ToUpperInvariant())));
        Assert.Equal(1, await _factory.WithDbAsync(db =>
            db.AuditLogs.CountAsync(a => a.EntityId == invite.PersonalStudentId && a.Action == "InviteAccepted")));
    }

    [Fact]
    public async Task ParallelHttpAccepts_OnlyOneSignsIn()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var clients = Enumerable.Range(0, 2).Select(_ => _factory.CreateBrowser()).ToList();
        var forms = new List<Dictionary<string, string>>();
        foreach (var client in clients)
        {
            // Dois aparelhos com o celular confirmado (a prova não depende do aparelho).
            var proof = await _factory.InviteProofAsync(invite.Token);
            var page = await client.GetAsync($"/convite/{invite.Token}");
            var form = NewAccountFields();
            form["New.PhoneProof"] = proof;
            form["_handler"] = "nova-conta";
            form["__RequestVerificationToken"] = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());
            forms.Add(form);
        }

        var responses = await Task.WhenAll(clients.Select((client, i) =>
            client.PostAsync($"/convite/{invite.Token}", new FormUrlEncodedContent(forms[i]))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(responses, r => AuthTestHelpers.AuthCookieHeader(r) is not null);
    }

    [Fact]
    public async Task AcceptPost_WithoutAntiforgery_IsRejected()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var form = NewAccountFields();
        form["_handler"] = "nova-conta";

        var response = await _client.PostAsync($"/convite/{invite.Token}", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertInviteUntouchedAsync(invite);
    }

    [Fact]
    public async Task Token_NeverAppearsInLogs_AndRequestPathIsRedacted()
    {
        var personalClient = _factory.CreateBrowser();
        await personalClient.PostLoginAsync(_personal.Email, _personal.Password);
        var created = await personalClient.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields());
        var token = StudentTestHelpers.InviteTokenIn(await (await personalClient.GetAsync(created.RedirectTarget())).TextAsync())!;

        await _client.GetAsync($"/convite/{token}");
        await AcceptNewAsync(_client, token, NewAccountFields());
        await _client.GetAsync($"/convite/{token}");

        Assert.DoesNotContain(_logs.Logs, l => l.Message.Contains(token, StringComparison.Ordinal)
                                               || l.Scope.Values.Any(v => v?.ToString()?.Contains(token, StringComparison.Ordinal) == true));
        Assert.Contains(_logs.Logs, l => l.Category == typeof(RequestLoggingMiddleware).FullName
                                         && l.Message.Contains("GET /convite/[redacted] respondeu 200"));
        Assert.Contains(_logs.Logs, l => l.Category == typeof(RequestLoggingMiddleware).FullName
                                         && l.Message.Contains("POST /convite/[redacted] respondeu 302"));
        Assert.DoesNotContain(_logs.Logs, l => l.Message.Contains(NewPassword));
    }

    [Fact]
    public async Task InvitePages_AreRateLimitedPerIp()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString,
            new Dictionary<string, string?> { ["Students:InviteRateLimit:PermitLimit"] = "2" });
        var client = factory.CreateBrowser();
        var token = new string('A', 43);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/convite/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/convite/{token}")).StatusCode);
        var limited = await client.GetAsync($"/convite/{token}");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Muitas tentativas", await limited.Content.ReadAsStringAsync());
        // Outras páginas não entram no limite do convite.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/entrar")).StatusCode);
    }

    [Theory]
    [InlineData("/termos", "Termos de Uso")]
    [InlineData("/privacidade", "Política de Privacidade")]
    public async Task LegalPages_ArePublicPlaceholders(string path, string title)
    {
        var response = await _client.GetAsync(path);
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(title, html);
        Assert.Contains("Texto em elaboração", html);
    }

    private async Task AssertInviteUntouchedAsync(CreatedInvite invite)
    {
        var (link, stored, userExists) = await _factory.WithDbAsync(async db => (
            await db.PersonalStudents.SingleAsync(s => s.Id == invite.PersonalStudentId),
            await db.StudentInvites.SingleAsync(i => i.PersonalStudentId == invite.PersonalStudentId),
            await db.PersonalStudents.Where(s => s.Id == invite.PersonalStudentId).Select(s => s.StudentUserId).SingleAsync() != null));

        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        Assert.Null(stored.UsedAtUtc);
        Assert.False(userExists);
    }
}
