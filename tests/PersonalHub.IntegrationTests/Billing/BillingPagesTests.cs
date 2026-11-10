using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Features.Billing.CloseBilling;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;

namespace PersonalHub.IntegrationTests.Billing;

/// <summary>
/// Telas do financeiro (personal) e dos pagamentos (aluno), com o relógio parado em 12/11/2026 12:00 (Brasília).
/// Outubro de 2026 já fechou para quem vence dia 10; quem vence dia 20 ainda vai fechar.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BillingPagesTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = SpClock(2026, 11, 12, 12);
    private static readonly DateOnly October = new(2026, 10, 1);

    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;
    private LoggedStudent _student = default!;
    private Guid _linkId;
    private Guid _periodId;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString).WithClock(Now);
        _personal = await _factory.LoginPersonalAsync();
        await _factory.SetPersonalNameAsync(_personal.Account.Id, "Carlos Lima");
        _linkId = (await _factory.CreateInviteAsync(_personal.Account.Id, name: "Mariana Lopes")).PersonalStudentId;
        _student = await _factory.LoginStudentAsync(await _factory.LinkStudentAsync(_linkId));

        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 5, 7));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 19, 18, 30));
        await _factory.AddLessonAsync(_linkId, Sp(2026, 10, 26));                      // ainda aguarda o aluno
        Assert.Equal(CloseLinkOutcome.Issued, await _factory.CloseAsync(_linkId, October, SpClock(2026, 11, 10)));
        _periodId = (await _factory.PeriodsOfAsync(_linkId)).Single().Id;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.TextAsync();
    }

    private async Task<Guid> AddLinkAsync(Guid personalUserId, string name, short dueDay)
    {
        var id = (await _factory.CreateInviteAsync(personalUserId, name: name)).PersonalStudentId;
        await _factory.SetBillingAsync(id, dueDay: dueDay);
        return id;
    }

    // ---------------------------------------------------------------- Personal

    [Fact]
    public async Task Financeiro_ShowsSummaryStatementsAndWhatStillCloses()
    {
        var later = await AddLinkAsync(_personal.Account.Id, "Bruno Alves", 20);
        await _factory.LinkStudentAsync(later);
        await _factory.AddApprovedLessonAsync(later, Sp(2026, 10, 7));

        var html = await PageAsync(_personal.Client, "/personal/financeiro");

        Assert.Contains("Novembro de 2026", html);
        Assert.Contains("Demonstrativos que vencem neste mês", html);
        Assert.Contains("Faturado", html);
        Assert.Contains("1 demonstrativo · 2 aulas", html);
        Assert.Contains("Mariana Lopes", html);
        Assert.Contains("Outubro · venceu dia 10 · 2 aulas", html);
        Assert.Contains("R$ 160,00", html);
        Assert.Contains("Aguardando pagamento", html);
        Assert.Contains($"/personal/financeiro/{_periodId}", html);

        Assert.Contains("Ainda vão fechar neste mês", html);
        Assert.Contains("Bruno Alves", html);
        Assert.Contains("Vence dia 20 · 1 aula confirmada", html);
        Assert.Contains("O valor final sai no vencimento.", html);
    }

    [Fact]
    public async Task Financeiro_PastMonthWithoutStatements_ShowsEmptyStateWithoutPreview()
    {
        var html = await PageAsync(_personal.Client, "/personal/financeiro?mes=2026-08");

        Assert.Contains("Agosto de 2026", html);
        Assert.Contains("Nenhum demonstrativo neste mês", html);
        Assert.DoesNotContain("Ainda vão fechar", html);
        Assert.DoesNotContain("Mariana Lopes", html);
    }

    [Fact]
    public async Task Financeiro_FutureMonth_FallsBackToCurrent()
    {
        var html = await PageAsync(_personal.Client, "/personal/financeiro?mes=2027-01");

        Assert.Contains("Novembro de 2026", html);
    }

    [Fact]
    public async Task Statement_ForPersonal_ShowsFrozenItemsAndPendingWarning()
    {
        var html = await PageAsync(_personal.Client, $"/personal/financeiro/{_periodId}");

        Assert.Contains("Mariana Lopes · Outubro de 2026", html);
        Assert.Contains("Aulas de 01/10 a 31/10 · emitido em 10/11/2026", html);
        Assert.Contains("2 aulas × R$ 80,00", html);
        Assert.Contains("Aula de 05/10 às 07:00", html);
        Assert.Contains("Aula de 19/10 às 18:30", html);
        Assert.Contains("1 aula de outubro ainda aguarda o aluno", html);
        Assert.Contains("Este demonstrativo não muda depois de emitido.", html);
        Assert.Contains($"/personal/alunos/{_linkId}", html);
    }

    [Fact]
    public async Task Statement_WithCreditAndLateLesson_ShowsBoth()
    {
        // Aula de outubro confirmada em 15/11 (depois do fechamento) e aula cobrada em outubro excluída depois.
        var late = await _factory.AddLessonAsync(_linkId, Sp(2026, 10, 30, 8));
        await _factory.ApproveAtAsync(late.Id, Sp(2026, 11, 15));
        var billed = (await _factory.ItemsOfAsync(_periodId)).First().LessonId!.Value;
        await _factory.DeleteLessonAtAsync(_personal.Account.Id, billed, SpClock(2026, 11, 16));
        await _factory.SetBillingAsync(_linkId, price: 90m);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 11, 3));
        await _factory.CloseAsync(_linkId, new DateOnly(2026, 11, 1), SpClock(2026, 12, 10));
        var november = (await _factory.PeriodsOfAsync(_linkId))[1];

        var html = await PageAsync(_personal.Client, $"/personal/financeiro/{november.Id}");

        Assert.Contains("Confirmada em 15/11, depois do fechamento de outubro", html);
        Assert.Contains("Créditos", html);
        Assert.Contains("Crédito da aula de 05/10 às 07:00", html);
        Assert.Contains("Excluída depois de cobrada em outubro", html);
        Assert.Contains("− R$ 80,00", html);
        Assert.Contains("2 aulas − R$ 80,00 de crédito", html);   // preços diferentes: só "N aulas"
        Assert.Equal(90m, november.TotalAmount);

        // O detalhe da aula excluída avisa que ela vira crédito, para o personal e para o aluno.
        const string creditNote = "Foi cobrada no demonstrativo de outubro. O valor volta como crédito no próximo demonstrativo.";
        Assert.Contains(creditNote, await PageAsync(_personal.Client, $"/personal/aulas/{billed}"));
        Assert.Contains(creditNote, await PageAsync(_student.Client, $"/aluno/aulas/{billed}"));
    }

    [Fact]
    public async Task StudentDetails_ShowsTheLatestStatement()
    {
        var html = await PageAsync(_personal.Client, $"/personal/alunos/{_linkId}");

        Assert.Contains("Último demonstrativo", html);
        Assert.Contains($"/personal/financeiro/{_periodId}", html);
        Assert.Contains("Vencimento 10/11/2026 · R$ 160,00", html);
    }

    [Fact]
    public async Task OtherPersonal_DoesNotSeeTheStatement()
    {
        var other = await _factory.LoginPersonalAsync();

        var details = await other.Client.GetAsync($"/personal/financeiro/{_periodId}");
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);

        var list = await PageAsync(other.Client, "/personal/financeiro");
        Assert.DoesNotContain("Mariana Lopes", list);
        Assert.Contains("Nenhum demonstrativo neste mês", list);
    }

    [Fact]
    public async Task Student_CannotOpenPersonalFinance()
    {
        var response = await _student.Client.GetAsync("/personal/financeiro");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------- Aluno

    [Fact]
    public async Task Payments_ShowsOpenPreviewAndPrevious_FromAllPersonals()
    {
        // Mesmo aluno com um segundo personal; e um demonstrativo antigo já pago.
        var secondPersonal = await _factory.CreateAccountAsync(Web.Infrastructure.Identity.AppRoles.Personal);
        await _factory.SetPersonalNameAsync(secondPersonal.Id, "Paula Reis");
        var secondLink = await AddLinkAsync(secondPersonal.Id, "Mariana Lopes", 5);
        await _factory.LinkStudentAsync(secondLink, _student.Account);
        await _factory.AddApprovedLessonAsync(secondLink, Sp(2026, 9, 8));
        await _factory.CloseAsync(secondLink, new DateOnly(2026, 9, 1), SpClock(2026, 10, 5));
        await _factory.WithDbAsync(db => db.BillingPeriods.Where(p => p.PersonalStudentId == secondLink)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, BillingPeriodStatus.Paid).SetProperty(p => p.PaidAtUtc, DateTime.UtcNow)));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 11, 3));

        var html = await PageAsync(_student.Client, "/aluno/pagamentos");

        Assert.Contains("Para pagar", html);
        Assert.Contains("Outubro · Carlos Lima", html);
        Assert.Contains("Venceu em 10/11", html);
        Assert.Contains("R$ 160,00", html);
        Assert.Contains("2 aulas confirmadas", html);
        Assert.Contains($"/aluno/pagamentos/{_periodId}", html);

        Assert.Contains("Novembro até agora", html);
        Assert.Contains("O demonstrativo sai em 10/12.", html);
        Assert.Contains("O demonstrativo sai em 05/12.", html);

        Assert.Contains("Anteriores", html);
        Assert.Contains("Setembro de 2026", html);
        Assert.Contains("Paula Reis", html);
    }

    [Fact]
    public async Task Previews_SkipAMonthAlreadyIssued_AfterTheDueDayChanged()
    {
        // Outubro fechou no dia 10; o vencimento passou para o dia 20. Não pode aparecer "Outubro" de novo.
        await _factory.SetBillingAsync(_linkId, dueDay: 20);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 11, 3));

        var student = await PageAsync(_student.Client, "/aluno/pagamentos");
        Assert.Contains("Novembro até agora", student);
        Assert.Contains("1 aula confirmada · O demonstrativo sai em 20/12.", student);
        Assert.DoesNotContain("Outubro até agora", student);

        var personal = await PageAsync(_personal.Client, "/personal/financeiro");
        Assert.DoesNotContain("Ainda vão fechar", personal);
    }

    [Fact]
    public async Task Payments_NothingOpen_ShowsAllPaid()
    {
        await _factory.WithDbAsync(db => db.BillingPeriods.Where(p => p.Id == _periodId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, BillingPeriodStatus.Paid).SetProperty(p => p.PaidAtUtc, DateTime.UtcNow)));

        var html = await PageAsync(_student.Client, "/aluno/pagamentos");

        Assert.Contains("Tudo pago", html);
        Assert.Contains("Nenhum demonstrativo em aberto.", html);
    }

    [Fact]
    public async Task Statement_ForStudent_ShowsTotalToPayAndHowToPay()
    {
        var html = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}");

        Assert.Contains("Outubro de 2026", html);
        Assert.Contains("Com Carlos Lima · vencimento 10/11/2026", html);
        Assert.Contains("Total a pagar", html);
        Assert.Contains("R$ 160,00", html);
        Assert.Contains("1 aula de outubro ainda aguarda sua resposta", html);
        Assert.Contains("href=\"/aluno/aulas\"", html);
        Assert.Contains("Pague direto ao Carlos Lima e avise aqui. Ele confirma quando receber.", html);
        Assert.Contains($"href=\"/aluno/pagamentos/{_periodId}/informar\"", html);
        Assert.Contains("Já paguei", html);
        Assert.DoesNotContain("chega na próxima etapa", html);
    }

    [Fact]
    public async Task OtherStudent_DoesNotSeeTheStatement()
    {
        var otherLink = (await _factory.CreateInviteAsync(_personal.Account.Id)).PersonalStudentId;
        var other = await _factory.LoginStudentAsync(await _factory.LinkStudentAsync(otherLink));

        var details = await other.Client.GetAsync($"/aluno/pagamentos/{_periodId}");
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);

        var list = await PageAsync(other.Client, "/aluno/pagamentos");
        Assert.DoesNotContain(_periodId.ToString(), list);
        Assert.Contains("Tudo pago", list);
    }

    [Fact]
    public async Task StudentHome_ShowsWhatIsOpen()
    {
        var html = await PageAsync(_student.Client, "/aluno");

        Assert.Contains("Demonstrativo para pagar", html);
        Assert.Contains("Total de R$ 160,00", html);
    }
}
