using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.ChangeLinkStatus;
using PersonalHub.Web.Features.Students.EditStudent;
using PersonalHub.Web.Features.Students.ManageInvite;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Students;

/// <summary>Um personal não vê nem altera alunos de outro personal, em nenhuma rota da gestão de alunos.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StudentIsolationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _owner = default!;
    private LoggedPersonal _intruder = default!;
    private CreatedInvite _invite = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _owner = await _factory.LoginPersonalAsync();
        _intruder = await _factory.LoginPersonalAsync();
        _invite = await _factory.CreateInviteAsync(_owner.Account.Id);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("")]
    [InlineData("/editar")]
    [InlineData("/vinculo")]
    [InlineData("/convite-criado")]
    public async Task Get_OtherPersonalsStudent_IsNotFound(string suffix)
    {
        var page = await _intruder.Client.GetAsync($"/personal/alunos/{_invite.PersonalStudentId}{suffix}");
        var html = await page.TextAsync();

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.DoesNotContain("Mariana", html);
        Assert.DoesNotContain(_invite.Email, html);
    }

    [Theory]
    [InlineData("", "novo-link")]
    [InlineData("", "cancelar-convite")]
    [InlineData("/editar", "editar-aluno")]
    [InlineData("/vinculo", "vinculo")]
    public async Task Post_OtherPersonalsStudent_ChangesNothing(string suffix, string formName)
    {
        var fields = new Dictionary<string, string>
        {
            ["acao"] = "inativar",
            ["Input.Name"] = "Invasor",
            ["Input.Email"] = "invasor@example.test",
            ["Input.Phone"] = "11987654321",
            ["Input.LessonPrice"] = "1",
            ["Input.BillingDueDay"] = "1",
            ["Input.Version"] = "0"
        };

        var response = await _intruder.Client.PostWithTokenFromAsync(
            $"/personal/alunos/{_invite.PersonalStudentId}{suffix}", formName, fields);
        var unknown = await _intruder.Client.PostWithTokenFromAsync(
            $"/personal/alunos/{Guid.NewGuid()}{suffix}", formName, fields);

        // A página não renderiza o formulário de um vínculo que não é do personal, e o Blazor recusa o envio com 400
        // antes de chamar o handler. A resposta é igual à de um id inexistente: não revela que o vínculo existe.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(unknown.StatusCode, response.StatusCode);
        Assert.DoesNotContain("Mariana", await response.TextAsync());
        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task Handlers_WithOtherPersonalsId_ReturnNotFound()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var intruderId = _intruder.Account.Id;
        var linkId = _invite.PersonalStudentId;

        var invites = services.GetRequiredService<InviteActionsHandler>();
        Assert.Equal(InviteActionOutcome.NotFound, (await invites.RenewAsync(intruderId, linkId, null, CancellationToken.None)).Outcome);
        Assert.Equal(InviteActionOutcome.NotFound, (await invites.CancelAsync(intruderId, linkId, null, CancellationToken.None)).Outcome);

        var status = services.GetRequiredService<ChangeLinkStatusHandler>();
        Assert.Equal(ChangeLinkStatusOutcome.NotFound, (await status.InactivateAsync(intruderId, linkId, null, CancellationToken.None)).Outcome);
        Assert.Equal(ChangeLinkStatusOutcome.NotFound, (await status.ReactivateAsync(intruderId, linkId, null, CancellationToken.None)).Outcome);

        var version = await _factory.WithDbAsync(db => db.PersonalStudents
            .Where(s => s.Id == linkId).Select(s => EF.Property<uint>(s, "xmin")).SingleAsync());
        var edit = services.GetRequiredService<EditStudentHandler>();
        var result = await edit.HandleAsync(new EditStudentCommand(intruderId, linkId, new EditStudentInput
        {
            Name = "Invasor", Email = "invasor@example.test", Phone = "11987654321", LessonPrice = "1",
            BillingDueDay = "1", Version = version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, null), CancellationToken.None);
        Assert.Equal(EditStudentOutcome.NotFound, result.Outcome);

        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task InactivePersonal_CannotManageOwnStudents()
    {
        await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _owner.Account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false)));

        await using var scope = _factory.Services.CreateAsyncScope();
        var status = scope.ServiceProvider.GetRequiredService<ChangeLinkStatusHandler>();
        var result = await status.InactivateAsync(_owner.Account.Id, _invite.PersonalStudentId, null, CancellationToken.None);

        Assert.Equal(ChangeLinkStatusOutcome.NotFound, result.Outcome);
        Assert.Equal(PersonalStudentStatus.Invited, (await _factory.LinkAsync(_invite.PersonalStudentId)).Status);
    }

    [Theory]
    [InlineData("/personal/alunos")]
    [InlineData("/personal/alunos/{0}")]
    [InlineData("/personal/alunos/{0}/editar")]
    [InlineData("/personal/alunos/{0}/vinculo")]
    public async Task StudentRole_CannotOpenPersonalPages(string path)
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(student.Email, student.Password);

        var response = await client.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, _invite.PersonalStudentId));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Mariana", await response.TextAsync());
    }

    private async Task AssertUntouchedAsync()
    {
        var link = await _factory.LinkAsync(_invite.PersonalStudentId);
        Assert.Equal("Mariana Lopes", link.StudentName);
        Assert.Equal(_invite.Email, link.StudentEmail);
        Assert.Equal(80m, link.LessonPrice);
        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        var invite = Assert.Single(await _factory.InvitesAsync(_invite.PersonalStudentId));
        Assert.Null(invite.RevokedAtUtc);
        Assert.Equal(["StudentInvited"], await _factory.AuditActionsAsync(_invite.PersonalStudentId));
    }
}
