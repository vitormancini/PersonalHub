using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.UnitTests.Infrastructure.WhatsApp;

public class MetaCloudWhatsAppSenderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static readonly WhatsAppOptions Options = new()
    {
        Provider = WhatsAppOptions.MetaProvider,
        ApiVersion = "v23.0",
        PhoneNumberId = "123456789012345",
        AccessToken = "token-secreto"
    };

    private static (MetaCloudWhatsAppSender Sender, FakeHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var http = new HttpClient(handler) { BaseAddress = new Uri(MetaCloudWhatsAppSender.BaseAddress) };
        return (new MetaCloudWhatsAppSender(http, Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<MetaCloudWhatsAppSender>.Instance), handler);
    }

    private static readonly WhatsAppTemplateMessage Utility = new(
        "+5511987652468", "ph_nova_aula", ["Mariana", "Carlos Lima", "12/11", "07:30"], "aluno/aulas/abc");

    [Fact]
    public async Task Send_PostsTemplateToPhoneNumberEndpoint_WithBearerToken()
    {
        var (sender, handler) = Create(HttpStatusCode.OK, """{"messaging_product":"whatsapp","messages":[{"id":"wamid.ABC"}]}""");

        var result = await sender.SendTemplateAsync(Utility, CancellationToken.None);

        Assert.Equal(WhatsAppSendStatus.Sent, result.Status);
        Assert.Equal("wamid.ABC", result.ProviderMessageId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://graph.facebook.com/v23.0/123456789012345/messages", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("token-secreto", handler.Request.Headers.Authorization.Parameter);

        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("whatsapp", (string?)body["messaging_product"]);
        Assert.Equal("5511987652468", (string?)body["to"]);
        Assert.Equal("template", (string?)body["type"]);
        Assert.Equal("ph_nova_aula", (string?)body["template"]!["name"]);
        Assert.Equal("pt_BR", (string?)body["template"]!["language"]!["code"]);

        var components = body["template"]!["components"]!.AsArray();
        Assert.Equal("body", (string?)components[0]!["type"]);
        Assert.Equal(["Mariana", "Carlos Lima", "12/11", "07:30"],
            components[0]!["parameters"]!.AsArray().Select(p => (string?)p!["text"]));
        Assert.Equal("button", (string?)components[1]!["type"]);
        Assert.Equal("url", (string?)components[1]!["sub_type"]);
        Assert.Equal("0", (string?)components[1]!["index"]);
        Assert.Equal("aluno/aulas/abc", (string?)components[1]!["parameters"]![0]!["text"]);
    }

    [Fact]
    public async Task Send_AuthenticationTemplate_PutsCodeInBodyAndCopyButton()
    {
        var (sender, handler) = Create(HttpStatusCode.OK, """{"messages":[{"id":"wamid.CODE"}]}""");

        await sender.SendTemplateAsync(new WhatsAppTemplateMessage("+5511987652468", "ph_codigo", [], null, "482913"),
            CancellationToken.None);

        var components = JsonNode.Parse(handler.Body!)!["template"]!["components"]!.AsArray();
        Assert.Equal("482913", (string?)components[0]!["parameters"]![0]!["text"]);
        Assert.Equal("url", (string?)components[1]!["sub_type"]);
        Assert.Equal("482913", (string?)components[1]!["parameters"]![0]!["text"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, null, WhatsAppSendStatus.Retryable, "http:429")]
    [InlineData(HttpStatusCode.InternalServerError, null, WhatsAppSendStatus.Retryable, "http:500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, WhatsAppSendStatus.Retryable, "http:503")]
    [InlineData(HttpStatusCode.BadRequest, 131000, WhatsAppSendStatus.Retryable, "meta:131000")]
    [InlineData(HttpStatusCode.BadRequest, 130429, WhatsAppSendStatus.Retryable, "meta:130429")]
    [InlineData(HttpStatusCode.BadRequest, 131026, WhatsAppSendStatus.Permanent, "meta:131026")]
    [InlineData(HttpStatusCode.BadRequest, 131031, WhatsAppSendStatus.Permanent, "meta:131031")]
    [InlineData(HttpStatusCode.BadRequest, 132001, WhatsAppSendStatus.Permanent, "meta:132001")]
    [InlineData(HttpStatusCode.BadRequest, 132012, WhatsAppSendStatus.Permanent, "meta:132012")]
    [InlineData(HttpStatusCode.BadRequest, 100, WhatsAppSendStatus.Permanent, "meta:100")]
    [InlineData(HttpStatusCode.InternalServerError, 131026, WhatsAppSendStatus.Permanent, "meta:131026")]
    [InlineData(HttpStatusCode.BadRequest, null, WhatsAppSendStatus.Permanent, "http:400")]
    public async Task Send_ClassifiesErrors_WithoutProviderText(
        HttpStatusCode status, int? code, WhatsAppSendStatus expected, string error)
    {
        var body = code is null
            ? "{}"
            : "{\"error\":{\"message\":\"(#" + code + ") número +5511987652468 inválido\",\"type\":\"OAuthException\",\"code\":" + code + ",\"fbtrace_id\":\"x\"}}";
        var (sender, _) = Create(status, body);

        var result = await sender.SendTemplateAsync(Utility, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(error, result.Error);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task Send_NetworkFailure_IsRetryable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("sem rede"));
        var sender = new MetaCloudWhatsAppSender(new HttpClient(handler) { BaseAddress = new Uri(MetaCloudWhatsAppSender.BaseAddress) },
            Microsoft.Extensions.Options.Options.Create(Options), NullLogger<MetaCloudWhatsAppSender>.Instance);

        var result = await sender.SendTemplateAsync(Utility, CancellationToken.None);

        Assert.Equal(WhatsAppSendStatus.Retryable, result.Status);
        Assert.Equal("rede", result.Error);
    }

    [Fact]
    public async Task Send_Timeout_IsRetryable()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout"));
        var sender = new MetaCloudWhatsAppSender(new HttpClient(handler) { BaseAddress = new Uri(MetaCloudWhatsAppSender.BaseAddress) },
            Microsoft.Extensions.Options.Options.Create(Options), NullLogger<MetaCloudWhatsAppSender>.Instance);

        var result = await sender.SendTemplateAsync(Utility, CancellationToken.None);

        Assert.Equal(WhatsAppSendStatus.Retryable, result.Status);
        Assert.Equal("timeout", result.Error);
    }
}
