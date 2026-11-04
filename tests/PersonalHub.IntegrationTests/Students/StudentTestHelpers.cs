using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.Web.Features.Students.CreateStudent;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Students;

public sealed record CreatedInvite(Guid PersonalStudentId, string Token, string Email);

public static partial class StudentTestHelpers
{
    public static Dictionary<string, string> ValidStudentFields(string? email = null) => new()
    {
        ["Input.Name"] = "Mariana Lopes",
        ["Input.Email"] = email ?? $"Aluna-{Guid.NewGuid():N}@Example.test ",
        ["Input.Phone"] = "(11) 98765-4321",
        ["Input.LessonPrice"] = "80,00",
        ["Input.BillingDueDay"] = "10"
    };

    /// <summary>Envia o formulário "Novo aluno" como o browser (antiforgery obtido no GET).</summary>
    public static Task<HttpResponseMessage> PostCreateStudentAsync(this HttpClient client, Dictionary<string, string> fields) =>
        client.PostFormAsync("/personal/alunos/novo", "novo-aluno", fields);

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string path, string formName, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>(fields)
        {
            ["_handler"] = formName,
            ["__RequestVerificationToken"] = token
        };
        return await client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    /// <summary>Cria o vínculo e o convite pelo handler, como o personal faria, e devolve o token puro.</summary>
    public static async Task<CreatedInvite> CreateInviteAsync(
        this WebApplicationFactory<Program> factory, Guid personalUserId, string? email = null, string name = "Mariana Lopes")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateStudentHandler>();
        email ??= $"aluna-{Guid.NewGuid():N}@example.test";
        var result = await handler.HandleAsync(new CreateStudentCommand(personalUserId, new CreateStudentInput
        {
            Name = name,
            Email = email,
            Phone = "11987654321",
            LessonPrice = "80",
            BillingDueDay = "10"
        }, "teste"), CancellationToken.None);

        Assert.Equal(CreateStudentOutcome.Succeeded, result.Outcome);
        return new CreatedInvite(result.PersonalStudentId!.Value, result.InviteToken!, email.Trim().ToLowerInvariant());
    }

    public static async Task<T> WithDbAsync<T>(this WebApplicationFactory<Program> factory, Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static Task WithDbAsync(this WebApplicationFactory<Program> factory, Func<AppDbContext, Task> action) =>
        factory.WithDbAsync(async db =>
        {
            await action(db);
            return true;
        });

    public static Task<int> SetInviteAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, DateTime? expiresAtUtc = null, DateTime? revokedAtUtc = null) =>
        factory.WithDbAsync(db => db.StudentInvites.Where(i => i.PersonalStudentId == personalStudentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ExpiresAtUtc, i => expiresAtUtc ?? i.ExpiresAtUtc)
                .SetProperty(i => i.RevokedAtUtc, i => revokedAtUtc ?? i.RevokedAtUtc)));

    /// <summary>Texto da página sem tags e com entidades decodificadas, para comparar mensagens.</summary>
    public static async Task<string> TextAsync(this HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    public static string? InviteTokenIn(string html)
    {
        var match = InviteLink().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"https://localhost/convite/([A-Za-z0-9_-]{43})")]
    private static partial Regex InviteLink();
}
