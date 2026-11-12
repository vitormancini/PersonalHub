using System.Text;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.UnitTests.Infrastructure.WhatsApp;

public class WhatsAppWebhookTests
{
    private const string Secret = "segredo-do-app";

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Signature_FromTheAppSecret_IsValid()
    {
        var body = Body("""{"entry":[]}""");

        Assert.True(WhatsAppWebhook.IsSignatureValid(body, WhatsAppWebhook.Sign(body, Secret), Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz")]
    [InlineData("sha1=abcdef")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Signature_MissingOrWrong_IsInvalid(string? header)
    {
        Assert.False(WhatsAppWebhook.IsSignatureValid(Body("{}"), header, Secret));
    }

    [Fact]
    public void Signature_OfAnotherBodyOrSecret_IsInvalid()
    {
        var body = Body("""{"a":1}""");

        Assert.False(WhatsAppWebhook.IsSignatureValid(Body("""{"a":2}"""), WhatsAppWebhook.Sign(body, Secret), Secret));
        Assert.False(WhatsAppWebhook.IsSignatureValid(body, WhatsAppWebhook.Sign(body, "outro"), Secret));
    }

    [Fact]
    public void ParseStatuses_ReadsIdStatusTimeAndError_AndIgnoresTheRest()
    {
        var updates = WhatsAppWebhook.ParseStatuses(Body("""
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp",
              "messages":[{"from":"5511987652468","text":{"body":"oi"}}],
              "statuses":[
                {"id":"wamid.A","status":"delivered","timestamp":"1762956000","recipient_id":"5511987652468"},
                {"id":"wamid.B","status":"failed","timestamp":"1762956001","errors":[{"code":131026,"title":"x"}]},
                {"id":"wamid.C","status":"deleted","timestamp":"1762956002"},
                {"status":"read"}
              ]}}]}]}
            """));

        Assert.Equal(2, updates.Count);
        Assert.Equal(new DeliveryUpdate("wamid.A", NotificationDeliveryStatus.Delivered,
            DateTimeOffset.FromUnixTimeSeconds(1762956000).UtcDateTime, null), updates[0]);
        Assert.Equal(NotificationDeliveryStatus.Failed, updates[1].Status);
        Assert.Equal(131026, updates[1].ErrorCode);
    }

    [Fact]
    public void Apply_OnlyMovesForward()
    {
        var now = new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
        var row = new NotificationOutbox { EventType = "ph_nova_aula", Payload = "{}", IdempotencyKey = "k" };

        WhatsAppWebhook.Apply(row, new DeliveryUpdate("w", NotificationDeliveryStatus.Read, now, null), now);
        WhatsAppWebhook.Apply(row, new DeliveryUpdate("w", NotificationDeliveryStatus.Delivered, now.AddMinutes(1), null), now);
        WhatsAppWebhook.Apply(row, new DeliveryUpdate("w", NotificationDeliveryStatus.Failed, now.AddMinutes(2), 131026), now);

        Assert.Equal(NotificationDeliveryStatus.Read, row.DeliveryStatus);
        Assert.Equal(now, row.ReadAtUtc);
        Assert.Equal(now, row.DeliveredAtUtc);
        Assert.Null(row.LastError);
    }

    [Fact]
    public void Apply_FailedBeforeDelivery_RecordsTheCode()
    {
        var now = new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
        var row = new NotificationOutbox
        {
            EventType = "ph_nova_aula", Payload = "{}", IdempotencyKey = "k", DeliveryStatus = NotificationDeliveryStatus.Sent
        };

        WhatsAppWebhook.Apply(row, new DeliveryUpdate("w", NotificationDeliveryStatus.Failed, now, 131026), now);

        Assert.Equal(NotificationDeliveryStatus.Failed, row.DeliveryStatus);
        Assert.Equal("meta:131026", row.LastError);
    }
}
