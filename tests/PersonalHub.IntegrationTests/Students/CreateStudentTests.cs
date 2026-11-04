using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.CreateStudent;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class CreateStudentTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;
    private TestAccount _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(_personal.Email, _personal.Password);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_PersistsLinkInviteAndAudit_AndRedirectsToInvitePage()
    {
        var fields = StudentTestHelpers.ValidStudentFields();
        var expectedEmail = fields["Input.Email"].Trim().ToLowerInvariant();

        var response = await _client.PostCreateStudentAsync(fields);

        var target = response.RedirectTarget();
        Assert.Matches("^/personal/alunos/[0-9a-f-]{36}/convite-criado$", target);
        var linkId = Guid.Parse(target.Split('/')[3]);

        var (link, invite, audit) = await _factory.WithDbAsync(async db => (
            await db.PersonalStudents.SingleAsync(s => s.Id == linkId),
            await db.StudentInvites.SingleAsync(i => i.PersonalStudentId == linkId),
            await db.AuditLogs.SingleAsync(a => a.EntityId == linkId)));

        var personalId = await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _personal.Id).Select(p => p.Id).SingleAsync());
        Assert.Equal(personalId, link.PersonalId);
        Assert.Equal("Mariana Lopes", link.StudentName);
        Assert.Equal(expectedEmail, link.StudentEmail);
        Assert.Equal("+5511987654321", link.StudentPhone);
        Assert.Equal(80.00m, link.LessonPrice);
        Assert.Equal(10, link.BillingDueDay);
        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        Assert.Null(link.StudentUserId);

        Assert.Equal(32, invite.TokenHash.Length);
        Assert.Null(invite.UsedAtUtc);
        Assert.InRange((invite.ExpiresAtUtc - invite.CreatedAtUtc).TotalDays, 6.999, 7.001);

        Assert.Equal("StudentInvited", audit.Action);
        Assert.Equal(_personal.Id, audit.ActorUserId);
        Assert.False(string.IsNullOrEmpty(audit.CorrelationId));
        Assert.DoesNotContain(expectedEmail, audit.Metadata);
        Assert.DoesNotContain("Mariana", audit.Metadata);
        Assert.DoesNotContain("98765", audit.Metadata);

        // A página seguinte mostra o link uma vez; o hash do token bate com o gravado.
        var page = await _client.GetAsync(target);
        var html = await page.TextAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Aluno cadastrado: Mariana Lopes", html);
        Assert.Contains("Aguardando aceite", html);
        var token = StudentTestHelpers.InviteTokenIn(html);
        Assert.NotNull(token);
        Assert.Equal(invite.TokenHash, InviteToken.TryHash(token));

        // O token puro não existe em nenhuma linha gravada.
        var metadata = await _factory.WithDbAsync(db => db.AuditLogs.Select(a => a.Metadata).ToListAsync());
        Assert.DoesNotContain(metadata, m => m != null && m.Contains(token, StringComparison.Ordinal));
        Assert.False(invite.TokenHash.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(token)));
        Assert.False(invite.TokenHash.AsSpan().SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
    }

    [Fact]
    public async Task InvitePage_ShowsWhatsAppLinkWithEncodedMessage_AndSecurityHeaders()
    {
        var response = await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields());
        var page = await _client.GetAsync(response.RedirectTarget());
        var html = await page.TextAsync();
        var token = StudentTestHelpers.InviteTokenIn(html)!;

        var expectedMessage = $"Olá, Mariana! Personal Teste convidou você para o PersonalHub. " +
                              $"Crie sua senha por este link (vale por 7 dias): https://localhost/convite/{token}";
        Assert.Contains($"href=\"https://wa.me/5511987654321?text={Uri.EscapeDataString(expectedMessage)}\"", html);
        Assert.Contains("Abre o seu WhatsApp com a mensagem pronta para Mariana.", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
        Assert.Equal("no-referrer", Assert.Single(page.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task InvitePage_OnRefresh_HidesTheLink()
    {
        var response = await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields());
        var target = response.RedirectTarget();
        await _client.GetAsync(target);

        var refresh = await _client.GetAsync(target);
        var html = await refresh.TextAsync();

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Contains("Aluno cadastrado: Mariana Lopes", html);
        Assert.Contains("o link só aparece uma vez", html);
        Assert.Null(StudentTestHelpers.InviteTokenIn(html));
        Assert.DoesNotContain("wa.me", html);
    }

    [Fact]
    public async Task InvitePage_OfAnotherPersonal_IsNotFound()
    {
        var response = await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields());
        var target = response.RedirectTarget();

        var other = await _factory.CreateAccountAsync(AppRoles.Personal);
        var otherClient = _factory.CreateBrowser();
        await otherClient.PostLoginAsync(other.Email, other.Password);
        var page = await otherClient.GetAsync(target);
        var html = await page.TextAsync();

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.DoesNotContain("Mariana", html);
    }

    [Fact]
    public async Task InvitePage_ForUnknownId_IsNotFound()
    {
        var page = await _client.GetAsync($"/personal/alunos/{Guid.NewGuid()}/convite-criado");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyFields_ShowsFieldErrors_AndSavesNothing()
    {
        var before = await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync());

        var response = await _client.PostCreateStudentAsync(new Dictionary<string, string>
        {
            ["Input.Name"] = " ",
            ["Input.Email"] = "",
            ["Input.Phone"] = "",
            ["Input.LessonPrice"] = "",
            ["Input.BillingDueDay"] = ""
        });
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o nome do aluno.", html);
        Assert.Contains("Informe o e-mail do aluno.", html);
        Assert.Contains("Informe um celular com DDD.", html);
        Assert.Contains("Informe o valor da aula.", html);
        Assert.Contains("Escolha o dia do vencimento.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Equal(before, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync()));
    }

    [Theory]
    [InlineData("Input.Phone", "98765-4321", "Informe um celular com DDD.")]
    [InlineData("Input.Email", "sem-arroba", "Informe um e-mail válido, como aluno@exemplo.com.")]
    [InlineData("Input.LessonPrice", "0", "Informe um valor entre 0,01 e 99.999,99.")]
    [InlineData("Input.LessonPrice", "100.000,00", "Informe um valor entre 0,01 e 99.999,99.")]
    [InlineData("Input.LessonPrice", "oitenta", "Informe um valor entre 0,01 e 99.999,99.")]
    [InlineData("Input.BillingDueDay", "32", "Escolha o dia do vencimento.")]
    [InlineData("Input.BillingDueDay", "0", "Escolha o dia do vencimento.")]
    public async Task Create_WithInvalidField_ShowsItsError(string field, string value, string message)
    {
        var fields = StudentTestHelpers.ValidStudentFields();
        fields[field] = value;

        var response = await _client.PostCreateStudentAsync(fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await response.TextAsync());
    }

    [Fact]
    public async Task Create_AcceptsBrazilianMoneyWithThousands()
    {
        var fields = StudentTestHelpers.ValidStudentFields();
        fields["Input.LessonPrice"] = "R$ 1.234,50";
        fields["Input.BillingDueDay"] = "31";

        var response = await _client.PostCreateStudentAsync(fields);

        var linkId = Guid.Parse(response.RedirectTarget().Split('/')[3]);
        var link = await _factory.WithDbAsync(db => db.PersonalStudents.SingleAsync(s => s.Id == linkId));
        Assert.Equal(1234.50m, link.LessonPrice);
        Assert.Equal(31, link.BillingDueDay);
    }

    [Fact]
    public async Task Create_DuplicateEmailForSamePersonal_IsRejected()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields(email));

        var response = await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields($"  {email.ToUpperInvariant()} "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Você já tem um aluno com este e-mail. Abra o cadastro dele para reenviar o convite.",
            await response.TextAsync());
        Assert.Equal(1, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync(s => s.StudentEmail == email)));
    }

    [Fact]
    public async Task Create_SameEmailForAnotherPersonal_IsAllowed()
    {
        var email = $"compartilhado-{Guid.NewGuid():N}@example.test";
        await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields(email));

        var other = await _factory.CreateAccountAsync(AppRoles.Personal);
        var otherClient = _factory.CreateBrowser();
        await otherClient.PostLoginAsync(other.Email, other.Password);
        var response = await otherClient.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields(email));

        Assert.Matches("/convite-criado$", response.RedirectTarget());
        Assert.Equal(2, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync(s => s.StudentEmail == email)));
    }

    [Fact]
    public async Task Create_SameEmailAfterLinkInactivated_IsAllowed()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == invite.PersonalStudentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PersonalStudentStatus.Inactive)
                .SetProperty(x => x.EndedAtUtc, DateTime.UtcNow)));

        var response = await _client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields(invite.Email));

        Assert.Matches("/convite-criado$", response.RedirectTarget());
    }

    [Fact]
    public async Task Create_ParallelDuplicates_OnlyOneIsSaved()
    {
        var email = $"paralelo-{Guid.NewGuid():N}@example.test";

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<CreateStudentHandler>();
            return await handler.HandleAsync(new CreateStudentCommand(_personal.Id,
                new CreateStudentInput
                {
                    Name = "Aluno", Email = email, Phone = "11987654321", LessonPrice = "80", BillingDueDay = "5"
                }, null), CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == CreateStudentOutcome.Succeeded);
        Assert.All(results.Where(r => r.Outcome != CreateStudentOutcome.Succeeded),
            r => Assert.Equal(CreateStudentOutcome.DuplicateEmail, r.Outcome));
        Assert.Equal(1, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync(s => s.StudentEmail == email)));
    }

    [Fact]
    public async Task Create_ByStudent_IsRedirectedAway_AndSavesNothing()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(student.Email, student.Password);

        var page = await client.GetAsync("/personal/alunos/novo");

        Assert.Equal("/aluno", page.RedirectTarget());
    }

    [Fact]
    public async Task Create_Anonymous_IsSentToLogin()
    {
        var page = await _factory.CreateBrowser().GetAsync("/personal/alunos/novo");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/entrar", page.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Create_WithoutAntiforgery_IsRejected()
    {
        var form = StudentTestHelpers.ValidStudentFields();
        form["_handler"] = "novo-aluno";

        var response = await _client.PostAsync("/personal/alunos/novo", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/personal", "Início")]
    [InlineData("/personal/alunos", "Alunos")]
    [InlineData("/personal/aulas", "Aulas")]
    [InlineData("/personal/financeiro", "Financeiro")]
    [InlineData("/personal/alunos/novo", "Alunos")]
    public async Task Shell_MarksTheActiveTab(string path, string activeTab)
    {
        var html = await (await _client.GetAsync(path)).TextAsync();

        Assert.Matches($"class=\"ph-tab is-active\" aria-current=\"page\">.*?<span>{activeTab}</span>",
            html.ReplaceLineEndings(" "));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "aria-current=\"page\""));
    }
}
