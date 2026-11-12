using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.IntegrationTests.Notifications;

/// <summary>Webhook da Meta (D-062): verificação, assinatura, atualização de entrega e desligado sem AppSecret.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppWebhookTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Secret = "segredo-do-app-de-teste";
    private const string VerifyToken = "verificar-123";

    private readonly FakeWhatsAppSender _sender = new();
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = Secret,
            ["WhatsApp:WebhookVerifyToken"] = VerifyToken
        }).WithWhatsApp(_sender);
        _client = _factory.CreateBrowser();
        await _factory.QuiesceOutboxAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<NotificationOutbox> SentRowAsync()
    {
        var row = await _factory.EnqueueAsync(DateTime.UtcNow.Date.AddHours(15));
        await _factory.WithDbAsync(db => db.NotificationOutbox.Where(o => o.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, NotificationStatus.Sent)
            .SetProperty(o => o.SentAtUtc, DateTime.UtcNow)
            .SetProperty(o => o.DeliveryStatus, NotificationDeliveryStatus.Sent)
            .SetProperty(o => o.ProviderMessageId, "wamid." + Guid.NewGuid().ToString("N"))));
        return await _factory.OutboxRowAsync(row.Id);
    }

    private static string StatusBody(string messageId, string status, long timestamp = 1762966800, int? error = null)
    {
        var errors = error is null ? "" : ",\"errors\":[{\"code\":" + error + "}]";
        return "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"id\":\"1\",\"changes\":[{\"field\":\"messages\"," +
               "\"value\":{\"messaging_product\":\"whatsapp\",\"statuses\":[{\"id\":\"" + messageId + "\",\"status\":\"" + status +
               "\",\"timestamp\":\"" + timestamp + "\"" + errors + "}]}}]}]}";
    }

    private Task<HttpResponseMessage> PostAsync(string body, string? signature)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhook.Path)
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        if (signature is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", signature);
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostSignedAsync(string body) =>
        PostAsync(body, WhatsAppWebhook.Sign(Encoding.UTF8.GetBytes(body), Secret));

    [Fact]
    public async Task Verify_WithRightToken_EchoesTheChallenge()
    {
        var response = await _client.GetAsync(
            $"{WhatsAppWebhook.Path}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=1158201444");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1158201444", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("subscribe", "errado")]
    [InlineData("unsubscribe", VerifyToken)]
    [InlineData("subscribe", "")]
    public async Task Verify_WithWrongTokenOrMode_IsForbidden(string mode, string token)
    {
        var response = await _client.GetAsync(
            $"{WhatsAppWebhook.Path}?hub.mode={mode}&hub.verify_token={token}&hub.challenge=123");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidSignature_UpdatesDeliveryAndRead()
    {
        var row = await SentRowAsync();

        Assert.Equal(HttpStatusCode.OK, (await PostSignedAsync(StatusBody(row.ProviderMessageId!, "delivered", 1762966800))).StatusCode);
        var delivered = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationDeliveryStatus.Delivered, delivered.DeliveryStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762966800).UtcDateTime, delivered.DeliveredAtUtc);

        await PostSignedAsync(StatusBody(row.ProviderMessageId!, "read", 1762966900));
        // Repetição fora de ordem (a Meta reenvia eventos): não volta para "entregue".
        await PostSignedAsync(StatusBody(row.ProviderMessageId!, "delivered", 1762967000));

        var read = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationDeliveryStatus.Read, read.DeliveryStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762966900).UtcDateTime, read.ReadAtUtc);
        Assert.Equal(delivered.DeliveredAtUtc, read.DeliveredAtUtc);
        Assert.Equal(NotificationStatus.Sent, read.Status);
    }

    [Fact]
    public async Task Post_FailedDelivery_IsRecordedAndAudited()
    {
        var row = await SentRowAsync();

        await PostSignedAsync(StatusBody(row.ProviderMessageId!, "failed", error: 131026));

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationDeliveryStatus.Failed, saved.DeliveryStatus);
        Assert.Equal("meta:131026", saved.LastError);
        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == row.Id));
        Assert.Equal("NotificationDeliveryFailed", audit.Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("assinatura")]
    public async Task Post_WithInvalidSignature_IsUnauthorized_AndChangesNothing(string? signature)
    {
        var row = await SentRowAsync();

        var response = await PostAsync(StatusBody(row.ProviderMessageId!, "read"), signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(NotificationDeliveryStatus.Sent, (await _factory.OutboxRowAsync(row.Id)).DeliveryStatus);
    }

    [Fact]
    public async Task Post_SignedWithAnotherSecret_IsUnauthorized()
    {
        var row = await SentRowAsync();
        var body = StatusBody(row.ProviderMessageId!, "read");

        var response = await PostAsync(body, WhatsAppWebhook.Sign(Encoding.UTF8.GetBytes(body), "outro-segredo"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_UnknownMessage_IsAcknowledged()
    {
        var response = await PostSignedAsync(StatusBody("wamid.desconhecido", "delivered"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_MalformedJson_WithValidSignature_IsBadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PostSignedAsync("{nao-e-json")).StatusCode);
    }

    [Fact]
    public async Task WithoutAppSecret_TheWebhookDoesNotExist()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString);
        var client = factory.CreateBrowser();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"{WhatsAppWebhook.Path}?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(WhatsAppWebhook.Path, new StringContent("{}"))).StatusCode);
    }

    [Fact]
    public async Task Webhook_IsRateLimitedPerIp()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = Secret,
            ["WhatsApp:WebhookVerifyToken"] = VerifyToken,
            ["WhatsApp:WebhookRateLimit:PermitLimit"] = "2"
        });
        var client = factory.CreateBrowser();
        var url = $"{WhatsAppWebhook.Path}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=1";

        await client.GetAsync(url);
        await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(url)).StatusCode);
    }
}
