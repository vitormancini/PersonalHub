using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Features.Students.AcceptInvite;
using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.IntegrationTests.Notifications;

/// <summary>Adapter falso: guarda as mensagens e responde o que o teste mandar.</summary>
public sealed class FakeWhatsAppSender : IWhatsAppSender
{
    public ConcurrentQueue<WhatsAppTemplateMessage> Sent { get; } = new();

    /// <summary>Resposta da próxima chamada (padrão: enviado com id novo).</summary>
    public Func<WhatsAppTemplateMessage, WhatsAppSendResult> Respond { get; set; } =
        _ => WhatsAppSendResult.Sent("wamid." + Guid.NewGuid().ToString("N"));

    public TimeSpan Delay { get; set; }

    public async Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        Sent.Enqueue(message);
        return Respond(message);
    }
}

public static partial class NotificationTestHelpers
{
    /// <summary>Linhas da outbox de um template, mais antigas primeiro.</summary>
    public static Task<List<NotificationOutbox>> OutboxAsync(
        this WebApplicationFactory<Program> factory, string template, Guid? recipientUserId = null) =>
        factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking()
            .Where(o => o.EventType == template && (recipientUserId == null || o.RecipientUserId == recipientUserId))
            .OrderBy(o => o.CreatedAtUtc).ToListAsync());

    public static Task<NotificationOutbox?> OutboxByKeyAsync(this WebApplicationFactory<Program> factory, string key) =>
        factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking().SingleOrDefaultAsync(o => o.IdempotencyKey == key));

    /// <summary>O código em texto puro da linha da outbox do código mais recente, lido como o worker leria.</summary>
    public static async Task<string> LatestCodeAsync(
        this WebApplicationFactory<Program> factory, PhoneCodePurpose purpose, Guid scopeId)
    {
        var code = await factory.WithDbAsync(db => db.PhoneVerificationCodes.AsNoTracking()
            .Where(c => c.Purpose == purpose && (c.PersonalStudentId == scopeId || c.UserId == scopeId))
            .OrderByDescending(c => c.CreatedAtUtc).FirstAsync());
        var row = await factory.OutboxByKeyAsync($"phone-code:{code.Id}");
        var payload = NotificationPayload.FromJson(row!.Payload)!;
        var protector = factory.Services.GetRequiredService<OutboxCodeProtector>();
        return protector.Unprotect(payload.ProtectedCode!)!;
    }

    public static Task<Guid> PersonalStudentOfAsync(this WebApplicationFactory<Program> factory, string token)
    {
        var hash = InviteToken.TryHash(token)!;
        return factory.WithDbAsync(db => db.StudentInvites.Where(i => i.TokenHash == hash)
            .Select(i => i.PersonalStudentId).SingleAsync());
    }

    /// <summary>
    /// O caminho real do aluno: "Enviar código pelo WhatsApp", lê o código (como chegaria no WhatsApp) e "Confirmar
    /// código". Devolve a prova que a página põe no formulário da conta.
    /// </summary>
    public static async Task<string> VerifyInvitePhoneAsync(
        this HttpClient client, WebApplicationFactory<Program> factory, string token)
    {
        var sent = await client.PostFormAsync($"/convite/{token}", "enviar-codigo", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var personalStudentId = await factory.PersonalStudentOfAsync(token);
        var code = await factory.LatestCodeAsync(PhoneCodePurpose.InviteAcceptance, personalStudentId);
        var verified = await client.PostFormAsync($"/convite/{token}", "confirmar-codigo",
            new Dictionary<string, string> { ["Code.Value"] = code });
        var html = await verified.Content.ReadAsStringAsync();
        return ProofIn(html) ?? throw new Xunit.Sdk.XunitException("Prova do código não encontrada na página.");
    }

    /// <summary>Prova criada direto pelo serviço, para testes que chamam o handler sem passar pela página.</summary>
    public static async Task<string> InviteProofAsync(this WebApplicationFactory<Program> factory, string token)
    {
        var hash = InviteToken.TryHash(token)!;
        var (inviteId, phone) = await factory.WithDbAsync(async db =>
        {
            var row = await (from i in db.StudentInvites
                             join s in db.PersonalStudents on i.PersonalStudentId equals s.Id
                             where i.TokenHash == hash
                             select new { i.Id, s.StudentPhone }).SingleAsync();
            return (row.Id, row.StudentPhone);
        });
        return factory.Services.GetRequiredService<InvitePhoneProof>().Create(inviteId, phone, DateTime.UtcNow);
    }

    public static string? ProofIn(string html)
    {
        var match = ProofInput().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    [GeneratedRegex("name=\"(?:New|Existing)\\.PhoneProof\" value=\"([^\"]+)\"")]
    private static partial Regex ProofInput();
}

/// <summary>Relógio que o teste avança.</summary>
public sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public static class NotificationTestApp
{
    /// <summary>Host com o adapter falso e o relógio do teste. O lote do dispatcher é grande para pegar tudo de uma vez.</summary>
    public static WebApplicationFactory<Program> WithWhatsApp(
        this WebApplicationFactory<Program> factory, FakeWhatsAppSender sender, TimeProvider? clock = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Notifications:Worker:BatchSize", "100000");
            builder.ConfigureServices(services => { });
            Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(builder, services =>
            {
                services.AddSingleton<IWhatsAppSender>(sender);
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });
        });

    /// <summary>Uma rodada do worker, como o BackgroundService faria.</summary>
    public static async Task<DispatchResult> DispatchAsync(this WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Encerra o que outros testes deixaram na fila (o banco é compartilhado e os testes da coleção rodam em sequência),
    /// para o dispatcher só tratar as linhas deste teste.
    /// </summary>
    public static Task<int> QuiesceOutboxAsync(this WebApplicationFactory<Program> factory) =>
        factory.WithDbAsync(db => db.NotificationOutbox
            .Where(o => o.Status == NotificationStatus.Pending || o.Status == NotificationStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, NotificationStatus.Failed)
                .SetProperty(o => o.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(o => o.LastError, "teste")));

    /// <summary>Linha pronta na outbox (sem passar por um handler).</summary>
    public static async Task<NotificationOutbox> EnqueueAsync(
        this WebApplicationFactory<Program> factory, DateTime nowUtc, string? phone = "+5511987652468",
        bool isAuthentication = false, string? code = null, DateTime? expiresAtUtc = null, string template = "ph_nova_aula")
    {
        var key = $"teste:{Guid.NewGuid():N}";
        await factory.WithDbAsync(async db =>
        {
            var protector = factory.Services.GetRequiredService<OutboxCodeProtector>();
            var payload = code is null
                ? new NotificationPayload(["Mariana", "Carlos Lima", "12/11", "07:30"], "aluno/aulas/x")
                : new NotificationPayload([], null, protector.Protect(code));
            Assert.True(await db.EnqueueNotificationAsync(
                new NotificationRequest(template, key, null, phone, payload, isAuthentication, expiresAtUtc), nowUtc,
                CancellationToken.None));
            await db.SaveChangesAsync();
        });
        return (await factory.OutboxByKeyAsync(key))!;
    }

    public static Task<NotificationOutbox> OutboxRowAsync(this WebApplicationFactory<Program> factory, Guid id) =>
        factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.Id == id));
}
