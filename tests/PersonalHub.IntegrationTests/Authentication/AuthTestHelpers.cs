using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Authentication;

public sealed record TestAccount(Guid Id, string Email, string Password);

public static partial class AuthTestHelpers
{
    public const string Password = "Senha-Forte1";

    /// <summary>Cria uma conta pelo UserManager (bloqueio habilitado como em produção) com a role pedida.</summary>
    /// <remarks>
    /// Alunos nascem com celular confirmado, como quem aceitou o convite na Fase 6; <paramref name="phoneConfirmed"/>
    /// falso simula o aluno que aceitou antes (D-059).
    /// </remarks>
    public static async Task<TestAccount> CreateAccountAsync(
        this WebApplicationFactory<Program> factory, string role, bool personalActive = true, bool? phoneConfirmed = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"conta-{Guid.NewGuid():N}@example.test";
        var confirmed = phoneConfirmed ?? role == AppRoles.Student;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            PhoneNumber = confirmed ? "+55" + Students.StudentTestHelpers.RandomPhone() : null,
            PhoneNumberConfirmed = confirmed
        };

        AssertSucceeded(await users.CreateAsync(user, Password));
        AssertSucceeded(await users.AddToRoleAsync(user, role));

        if (role == AppRoles.Personal)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PersonalProfiles.Add(new PersonalProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DisplayName = "Personal Teste",
                IsActive = personalActive,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        return new TestAccount(user.Id, email, Password);
    }

    /// <summary>Abre /entrar (para obter o antiforgery) e envia o formulário como o browser faria.</summary>
    public static async Task<HttpResponseMessage> PostLoginAsync(
        this HttpClient client, string email, string password, bool rememberMe = false, string path = "/entrar")
    {
        var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = password
        };
        if (rememberMe)
        {
            fields["Input.RememberMe"] = "true";
        }

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryInput().Match(html);
        Assert.True(match.Success, "Token antiforgery não encontrado na página.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>
    /// Destino do redirecionamento como caminho local. O Blazor gera Location absoluto (https://localhost/...),
    /// o middleware de cookie gera relativo; em ambos os casos o host precisa ser o da própria aplicação.
    /// </summary>
    public static string RedirectTarget(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        if (!location.IsAbsoluteUri)
        {
            return location.OriginalString;
        }

        Assert.Equal("localhost", location.Host);
        return location.PathAndQuery;
    }

    public static string? AuthCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith("__Host-PersonalHub.Auth=", StringComparison.Ordinal))
            : null;

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();
}
