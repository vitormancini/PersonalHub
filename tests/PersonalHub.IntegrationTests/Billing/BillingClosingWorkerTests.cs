using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Billing;

/// <summary>Agendamento (D-047): com o worker ligado, o fechamento devido roda logo na partida do app.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BillingClosingWorkerTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Worker_ClosesDueLinksOnStartup()
    {
        // Prepara com o worker desligado: vencimento hoje e uma aula confirmada no mês passado.
        var today = SaoPaulo.ToLocalDate(DateTime.UtcNow);
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        Guid linkId;
        await using (var setup = TestApp.Create(fixture.ConnectionString))
        {
            var personal = await setup.CreateAccountAsync(AppRoles.Personal);
            linkId = (await setup.CreateInviteAsync(personal.Id)).PersonalStudentId;
            await setup.LinkStudentAsync(linkId);
            await setup.SetBillingAsync(linkId, dueDay: (short)today.Day);
            await setup.AddApprovedLessonAsync(linkId, LessonTestHelpers.Utc(lastMonth.AddDays(1), 10));
        }

        await using var app = TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["Billing:Closing:Enabled"] = "true"
        });
        _ = app.Services; // inicia o host e os hosted services

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && (await app.PeriodsOfAsync(linkId)).Count == 0)
        {
            await Task.Delay(200);
        }

        var period = Assert.Single(await app.PeriodsOfAsync(linkId));
        Assert.Equal(lastMonth, period.ReferenceMonth);
        Assert.Equal(today, period.DueDate);
    }
}
