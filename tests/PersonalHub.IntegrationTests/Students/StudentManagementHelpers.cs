using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Students;

public sealed record LoggedPersonal(TestAccount Account, HttpClient Client);

/// <summary>Atalhos dos testes da gestão de alunos (Fase 2).</summary>
public static class StudentManagementHelpers
{
    public static async Task<LoggedPersonal> LoginPersonalAsync(this WebApplicationFactory<Program> factory)
    {
        var account = await factory.CreateAccountAsync(AppRoles.Personal);
        var client = factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);
        return new LoggedPersonal(account, client);
    }

    /// <summary>Simula o aceite do convite: conta de aluno ligada, vínculo Active e convite usado.</summary>
    public static async Task<Guid> ActivateAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId)
    {
        var student = await factory.CreateAccountAsync(AppRoles.Student);
        await factory.WithDbAsync(async db =>
        {
            await db.StudentInvites.Where(i => i.PersonalStudentId == personalStudentId && i.UsedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAtUtc, DateTime.UtcNow));
            await db.PersonalStudents.Where(s => s.Id == personalStudentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.StudentUserId, student.Id)
                    .SetProperty(x => x.Status, PersonalStudentStatus.Active)
                    .SetProperty(x => x.ActivatedAtUtc, DateTime.UtcNow));
        });
        return student.Id;
    }

    public static Task<int> SetInactiveAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId, DateTime endedAtUtc) =>
        factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == personalStudentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, PersonalStudentStatus.Inactive)
                .SetProperty(x => x.EndedAtUtc, endedAtUtc)));

    public static Task<PersonalStudent> LinkAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId) =>
        factory.WithDbAsync(db => db.PersonalStudents.AsNoTracking().SingleAsync(s => s.Id == personalStudentId));

    public static Task<List<StudentInvite>> InvitesAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId) =>
        factory.WithDbAsync(db => db.StudentInvites.AsNoTracking()
            .Where(i => i.PersonalStudentId == personalStudentId).OrderBy(i => i.CreatedAtUtc).ToListAsync());

    public static Task<List<string>> AuditActionsAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId) =>
        factory.WithDbAsync(db => db.AuditLogs.Where(a => a.EntityId == personalStudentId)
            .OrderBy(a => a.OccurredAtUtc).Select(a => a.Action).ToListAsync());

    /// <summary>
    /// POST com um antiforgery válido do próprio usuário, obtido em outra página. Serve para forjar envios a páginas
    /// que não mostram o formulário (por exemplo, o vínculo de outro personal).
    /// </summary>
    public static async Task<HttpResponseMessage> PostWithTokenFromAsync(
        this HttpClient client, string path, string formName, Dictionary<string, string>? fields = null,
        string tokenPage = "/personal/alunos/novo")
    {
        var page = await client.GetAsync(tokenPage);
        var token = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>())
        {
            ["_handler"] = formName,
            ["__RequestVerificationToken"] = token
        };
        return await client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    /// <summary>Abre a página do convite como o aluno faria (cliente anônimo) e devolve o texto.</summary>
    public static async Task<string> InvitePageTextAsync(this WebApplicationFactory<Program> factory, string token)
    {
        var anonymous = factory.CreateBrowser();
        var page = await anonymous.GetAsync($"/convite/{token}");
        return await page.TextAsync();
    }
}
