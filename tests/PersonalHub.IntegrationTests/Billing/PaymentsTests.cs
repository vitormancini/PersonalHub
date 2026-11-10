using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Billing.Payments;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;

namespace PersonalHub.IntegrationTests.Billing;

/// <summary>
/// Pagamentos informados (Fase 5, D-049 a D-053): aluno informa, personal confirma, recusa ou registra direto. Relógio
/// parado em 12/11/2026 12:00 (Brasília); o demonstrativo de outubro (R$ 160,00) venceu dia 10.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PaymentsTests(DatabaseFixture fixture) : IAsyncLifetime
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
        Assert.Equal(CloseLinkOutcome.Issued, await _factory.CloseAsync(_linkId, October, SpClock(2026, 11, 10)));
        _periodId = (await _factory.PeriodsOfAsync(_linkId)).Single().Id;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ---------------------------------------------------------------- Atalhos

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.TextAsync();
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, Guid periodId, string? note = null) =>
        client.PostWithTokenFromAsync($"/aluno/pagamentos/{periodId}/informar", "informar",
            note is null ? null : new Dictionary<string, string> { ["observacao"] = note }, tokenPage: "/aluno/pagamentos");

    private static Task<HttpResponseMessage> ConfirmFromListAsync(HttpClient client, Guid? periodId, string query = "") =>
        client.PostWithTokenFromAsync($"/personal/financeiro{query}", "confirmar",
            periodId is null ? null : new Dictionary<string, string> { ["demonstrativo"] = periodId.Value.ToString() },
            tokenPage: "/personal/financeiro");

    private static Task<HttpResponseMessage> ConfirmFromDetailsAsync(HttpClient client, Guid periodId) =>
        client.PostWithTokenFromAsync($"/personal/financeiro/{periodId}", "confirmar", tokenPage: "/personal/financeiro");

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, Guid periodId, string? message = null) =>
        client.PostWithTokenFromAsync($"/personal/financeiro/{periodId}/nao-recebi", "nao-recebi",
            message is null ? null : new Dictionary<string, string> { ["mensagem"] = message }, tokenPage: "/personal/financeiro");

    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid periodId) =>
        client.PostWithTokenFromAsync($"/personal/financeiro/{periodId}/registrar", "registrar", tokenPage: "/personal/financeiro");

    private async Task<T> InScopeAsync<T>(Func<PaymentHandler, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PaymentHandler>());
    }

    private Task<BillingPeriod> PeriodAsync(Guid? id = null) =>
        _factory.WithDbAsync(db => db.BillingPeriods.AsNoTracking().SingleAsync(p => p.Id == (id ?? _periodId)));

    private Task<List<Payment>> PaymentsAsync(Guid? id = null) =>
        _factory.WithDbAsync(db => db.Payments.AsNoTracking().Where(p => p.BillingPeriodId == (id ?? _periodId))
            .OrderBy(p => p.ReportedAtUtc).ThenBy(p => p.DecidedAtUtc == null).ThenBy(p => p.DecidedAtUtc).ToListAsync());

    private Task<List<(string Action, string? Metadata)>> PaymentAuditAsync() =>
        _factory.WithDbAsync(async db => (await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "Payment")
                .Where(a => db.Payments.Any(p => p.Id == a.EntityId && p.BillingPeriodId == _periodId))
                .OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.CreatedAtUtc)
                .Select(a => new { a.Action, a.Metadata }).ToListAsync())
            .Select(a => (a.Action, a.Metadata)).ToList());

    private async Task AssertUntouchedAsync()
    {
        Assert.Equal(BillingPeriodStatus.Issued, (await PeriodAsync()).Status);
        Assert.Empty(await PaymentsAsync());
    }

    private Task<PaymentOutcome> ReportByHandlerAsync(string? note = null) =>
        InScopeAsync(h => h.ReportAsync(_student.Account.Id, _periodId, note, null, CancellationToken.None));

    /// <summary>Demonstrativo extra do mesmo vínculo, gravado direto (outro mês de referência).</summary>
    private async Task<Guid> AddPeriodAsync(DateOnly referenceMonth, decimal total = 120m, BillingPeriodStatus status = BillingPeriodStatus.Issued)
    {
        var period = TestData.NewBillingPeriod(_linkId, referenceMonth);
        period.TotalAmount = total;
        period.Status = status;
        period.PaidAtUtc = status == BillingPeriodStatus.Paid ? period.IssuedAtUtc : null;
        await _factory.WithDbAsync(async db =>
        {
            db.BillingPeriods.Add(period);
            await db.SaveChangesAsync();
        });
        return period.Id;
    }

    // ---------------------------------------------------------------- Aluno informa

    [Fact]
    public async Task Student_ReportsPayment_FromConfirmationPage()
    {
        var page = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}/informar");
        Assert.Contains("Você já pagou o Carlos Lima?", page);
        Assert.Contains("Outubro · valor total R$ 160,00", page);
        Assert.Contains("O pagamento é do valor total do demonstrativo.", page);
        Assert.Contains("Observação para o Carlos Lima", page);
        Assert.Contains("Sim, informar pagamento", page);
        Assert.Contains("Ainda não paguei", page);

        var response = await ReportAsync(_student.Client, _periodId, "  Paguei por Pix no dia 11  ");
        Assert.Equal($"/aluno/pagamentos/{_periodId}?aviso=informado", response.RedirectTarget());

        var period = await PeriodAsync();
        Assert.Equal(BillingPeriodStatus.PaymentReported, period.Status);
        Assert.Null(period.PaidAtUtc);
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(PaymentStatus.Reported, payment.Status);
        Assert.Equal(160m, payment.Amount);
        Assert.Equal(_student.Account.Id, payment.ReportedByUserId);
        Assert.Equal(Now.UtcDateTime, payment.ReportedAtUtc);
        Assert.Equal("Paguei por Pix no dia 11", payment.Notes);

        // Auditoria sem a observação do aluno.
        var audit = Assert.Single(await PaymentAuditAsync());
        Assert.Equal("PaymentReported", audit.Action);
        Assert.DoesNotContain("Pix", audit.Metadata);
        Assert.Contains(_periodId.ToString(), audit.Metadata);

        var details = await PageAsync(_student.Client, response.RedirectTarget());
        Assert.Contains("Pagamento informado.", details);
        Assert.Contains(">Pagamento informado<", details);
        Assert.Contains("Você informou o pagamento em 12/11 às 12:00. Agora o Carlos Lima confere e confirma o recebimento.", details);
        Assert.Contains("Pagamento informado por você", details);
        Assert.Contains("Aguardando Carlos Lima confirmar", details);
        Assert.Contains("Total a pagar", details);
        Assert.DoesNotContain("Já paguei", details);

        var list = await PageAsync(_student.Client, "/aluno/pagamentos");
        Assert.Contains("Para pagar", list);
        Assert.Contains(">Pagamento informado<", list);
        Assert.Contains($"/aluno/pagamentos/{_periodId}", list);

        var home = await PageAsync(_student.Client, "/aluno");
        Assert.Contains("Demonstrativo para pagar", home);
        Assert.Contains("Pagamento informado. Aguardando o personal confirmar.", home);
    }

    [Fact]
    public async Task Student_ReportsTwice_SecondIsIgnoredWithNotice()
    {
        Assert.Equal($"/aluno/pagamentos/{_periodId}?aviso=informado", (await ReportAsync(_student.Client, _periodId)).RedirectTarget());

        var again = await ReportAsync(_student.Client, _periodId, "de novo");
        Assert.Equal($"/aluno/pagamentos/{_periodId}?aviso=ja-informado", again.RedirectTarget());
        Assert.Contains("Você já tinha informado este pagamento.", await PageAsync(_student.Client, again.RedirectTarget()));

        // A página de informar de um demonstrativo já informado volta para o detalhe.
        Assert.Equal($"/aluno/pagamentos/{_periodId}",
            (await _student.Client.GetAsync($"/aluno/pagamentos/{_periodId}/informar")).RedirectTarget());

        var payment = Assert.Single(await PaymentsAsync());
        Assert.Null(payment.Notes);
        Assert.Single(await PaymentAuditAsync());
    }

    [Fact]
    public async Task Student_ReportsInParallel_OnlyOnePaymentIsCreated()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ReportByHandlerAsync()));

        Assert.Single(results, r => r == PaymentOutcome.Succeeded);
        Assert.Equal(3, results.Count(r => r == PaymentOutcome.AlreadyReported));
        Assert.Single(await PaymentsAsync());
        Assert.Single(await PaymentAuditAsync());
        Assert.Equal(BillingPeriodStatus.PaymentReported, (await PeriodAsync()).Status);
    }

    [Fact]
    public async Task Student_NoteTooLong_ShowsErrorAndChangesNothing()
    {
        var response = await ReportAsync(_student.Client, _periodId, new string('a', Payment.TextMaxLength + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A observação pode ter até 500 caracteres.", await response.TextAsync());
        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task Student_CannotReportZeroTotal_AlreadyPaid()
    {
        var zero = await AddPeriodAsync(new DateOnly(2026, 9, 1), total: 0m, status: BillingPeriodStatus.Paid);

        var outcome = await InScopeAsync(h => h.ReportAsync(_student.Account.Id, zero, null, null, CancellationToken.None));
        Assert.Equal(PaymentOutcome.AlreadyPaid, outcome);
        Assert.Equal($"/aluno/pagamentos/{zero}", (await _student.Client.GetAsync($"/aluno/pagamentos/{zero}/informar")).RedirectTarget());

        var details = await PageAsync(_student.Client, $"/aluno/pagamentos/{zero}");
        Assert.DoesNotContain("Já paguei", details);
        Assert.DoesNotContain("Pagamento confirmado pelo", details);
        Assert.Empty(await PaymentsAsync(zero));
    }

    [Fact]
    public async Task InactiveLink_StillReportsAndConfirms()
    {
        await _factory.SetInactiveAsync(_linkId, Now.UtcDateTime.AddDays(-1));

        Assert.Equal(PaymentOutcome.Succeeded, await ReportByHandlerAsync());
        Assert.Equal(PaymentOutcome.Succeeded,
            await InScopeAsync(h => h.ConfirmAsync(_personal.Account.Id, _periodId, null, CancellationToken.None)));
        Assert.Equal(BillingPeriodStatus.Paid, (await PeriodAsync()).Status);
    }

    // ---------------------------------------------------------------- Isolamento

    [Fact]
    public async Task OtherStudent_CannotReportSomeoneElsesStatement()
    {
        var otherLink = (await _factory.CreateInviteAsync(_personal.Account.Id)).PersonalStudentId;
        var other = await _factory.LoginStudentAsync(await _factory.LinkStudentAsync(otherLink));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/aluno/pagamentos/{_periodId}/informar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(other.Client, _periodId, "x")).StatusCode);
        Assert.Equal(PaymentOutcome.NotFound,
            await InScopeAsync(h => h.ReportAsync(other.Account.Id, _periodId, null, null, CancellationToken.None)));
        // O personal também não pode informar como se fosse o aluno.
        Assert.Equal(PaymentOutcome.NotFound,
            await InScopeAsync(h => h.ReportAsync(_personal.Account.Id, _periodId, null, null, CancellationToken.None)));

        await AssertUntouchedAsync();
    }

    [Fact]
    public async Task OtherPersonal_CannotConfirmRejectOrRecord()
    {
        await ReportByHandlerAsync();
        var other = await _factory.LoginPersonalAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/personal/financeiro/{_periodId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/personal/financeiro/{_periodId}/nao-recebi")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/personal/financeiro/{_periodId}/registrar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmFromDetailsAsync(other.Client, _periodId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(other.Client, _periodId, "x")).StatusCode);

        // Pela lista: volta sem aviso, sem revelar nada.
        Assert.Equal("/personal/financeiro?mes=2026-11", (await ConfirmFromListAsync(other.Client, _periodId)).RedirectTarget());
        Assert.DoesNotContain("Mariana", await PageAsync(other.Client, "/personal/financeiro"));
        Assert.DoesNotContain("Pagamento para confirmar", await PageAsync(other.Client, "/personal"));

        Assert.Equal(PaymentOutcome.NotFound,
            await InScopeAsync(h => h.RecordAsync(other.Account.Id, _periodId, null, CancellationToken.None)));
        // O aluno também não decide o próprio pagamento.
        Assert.Equal(PaymentOutcome.NotFound,
            await InScopeAsync(h => h.ConfirmAsync(_student.Account.Id, _periodId, null, CancellationToken.None)));

        Assert.Equal(BillingPeriodStatus.PaymentReported, (await PeriodAsync()).Status);
        Assert.Equal(PaymentStatus.Reported, Assert.Single(await PaymentsAsync()).Status);
    }

    // ---------------------------------------------------------------- Personal confirma

    [Fact]
    public async Task Personal_SeesPendingAndConfirmsFromList()
    {
        await ReportByHandlerAsync("Paguei por Pix");

        var before = await PageAsync(_personal.Client, "/personal/financeiro");
        Assert.Contains("Pagamentos para confirmar", before);
        Assert.Contains("Outubro · informou em 12/11 às 12:00", before);
        Assert.Contains("\"Paguei por Pix\"", before);
        Assert.Contains($"/personal/financeiro/{_periodId}/nao-recebi", before);
        Assert.Contains(">Pagamento informado<", before);
        Assert.Contains("Confirmar o pagamento de Mariana", before);

        var home = await PageAsync(_personal.Client, "/personal");
        Assert.Contains("Pagamento para confirmar", home);

        var response = await ConfirmFromListAsync(_personal.Client, _periodId);
        Assert.Equal("/personal/financeiro?mes=2026-11&aviso=recebido", response.RedirectTarget());

        var after = await PageAsync(_personal.Client, response.RedirectTarget());
        Assert.Contains("Recebimento confirmado.", after);
        Assert.DoesNotContain("Pagamentos para confirmar", after);
        Assert.Contains(">Pago<", after);
        Assert.DoesNotContain("Pagamento para confirmar", await PageAsync(_personal.Client, "/personal"));

        var period = await PeriodAsync();
        Assert.Equal(BillingPeriodStatus.Paid, period.Status);
        Assert.Equal(Now.UtcDateTime, period.PaidAtUtc);
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(_personal.Account.Id, payment.DecidedByUserId);
        Assert.Equal(["PaymentConfirmed", "PaymentReported"], (await PaymentAuditAsync()).Select(a => a.Action).Order());

        var student = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}");
        Assert.Contains("Pagamento confirmado pelo Carlos Lima em 12/11. Este demonstrativo está quitado.", student);
        Assert.Contains("Total pago", student);
        Assert.Contains("Recebimento confirmado por Carlos Lima", student);
        Assert.DoesNotContain("Já paguei", student);
        Assert.Contains("Tudo pago", await PageAsync(_student.Client, "/aluno/pagamentos"));
    }

    [Fact]
    public async Task Financeiro_SummaryMovesFromReceivableToReceived()
    {
        static string Summary(string html) => html[html.IndexOf("Resumo do mês", StringComparison.Ordinal)..];

        var open = Summary(await PageAsync(_personal.Client, "/personal/financeiro"));
        Assert.Matches(@"Recebido</dt>\s*<dd[^>]*>R\$ 0,00", open);
        Assert.Matches(@"A receber</dt>\s*<dd[^>]*>R\$ 160,00", open);

        await ReportByHandlerAsync();
        var reported = Summary(await PageAsync(_personal.Client, "/personal/financeiro"));
        Assert.Matches(@"A receber</dt>\s*<dd[^>]*>R\$ 160,00", reported);

        await ConfirmFromListAsync(_personal.Client, _periodId);
        var paid = Summary(await PageAsync(_personal.Client, "/personal/financeiro"));
        Assert.Matches(@"Recebido</dt>\s*<dd[^>]*>R\$ 160,00", paid);
        Assert.Matches(@"A receber</dt>\s*<dd[^>]*>R\$ 0,00", paid);
    }

    [Fact]
    public async Task Financeiro_PendingSection_ListsAllMonths()
    {
        var august = await AddPeriodAsync(new DateOnly(2026, 8, 1));
        Assert.Equal(PaymentOutcome.Succeeded,
            await InScopeAsync(h => h.ReportAsync(_student.Account.Id, august, null, null, CancellationToken.None)));

        // Agosto vence em setembro, mas aparece em "Pagamentos para confirmar" mesmo vendo novembro (e o mês de julho).
        var november = await PageAsync(_personal.Client, "/personal/financeiro");
        Assert.Contains("Agosto · informou em 12/11 às 12:00", november);
        Assert.Contains("Agosto · informou", await PageAsync(_personal.Client, "/personal/financeiro?mes=2026-07"));

        var response = await ConfirmFromListAsync(_personal.Client, august, "?mes=2026-07");
        Assert.Equal("/personal/financeiro?mes=2026-07&aviso=recebido", response.RedirectTarget());
        Assert.Equal(BillingPeriodStatus.Paid, (await PeriodAsync(august)).Status);
    }

    [Fact]
    public async Task ConfirmFromList_WithoutStatement_ShowsNotSent()
    {
        var response = await ConfirmFromListAsync(_personal.Client, null);

        Assert.Equal("/personal/financeiro?mes=2026-11&aviso=nao-enviado", response.RedirectTarget());
        Assert.Contains("Não foi possível confirmar o recebimento.", await PageAsync(_personal.Client, response.RedirectTarget()));
    }

    [Fact]
    public async Task Personal_ConfirmsFromDetails_AndConfirmIsFinal()
    {
        await ReportByHandlerAsync("Pix");

        var details = await PageAsync(_personal.Client, $"/personal/financeiro/{_periodId}");
        Assert.Contains("Mariana informou o pagamento", details);
        Assert.Contains("12/11 às 12:00 · R$ 160,00", details);
        Assert.Contains("\"Pix\"", details);
        Assert.Contains("Confira na sua conta antes de confirmar.", details);
        Assert.Contains("Confirmar recebimento", details);
        Assert.Contains("Histórico do pagamento", details);
        Assert.Contains("Pagamento informado por Mariana", details);

        var response = await ConfirmFromDetailsAsync(_personal.Client, _periodId);
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=recebido", response.RedirectTarget());
        var paid = await PageAsync(_personal.Client, response.RedirectTarget());
        Assert.Contains("Você confirmou o recebimento em 12/11. Mariana vê o demonstrativo como pago.", paid);
        Assert.Contains("Recebimento confirmado por você", paid);
        Assert.DoesNotContain("Registrar recebimento", paid);

        // Confirmar de novo, recusar ou registrar depois de pago não muda nada (D-053).
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=ja-pago", (await ConfirmFromDetailsAsync(_personal.Client, _periodId)).RedirectTarget());
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=ja-pago", (await RejectAsync(_personal.Client, _periodId)).RedirectTarget());
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=ja-pago", (await RecordAsync(_personal.Client, _periodId)).RedirectTarget());
        Assert.Equal($"/aluno/pagamentos/{_periodId}?aviso=ja-pago", (await ReportAsync(_student.Client, _periodId)).RedirectTarget());
        Assert.Equal($"/personal/financeiro/{_periodId}",
            (await _personal.Client.GetAsync($"/personal/financeiro/{_periodId}/nao-recebi")).RedirectTarget());

        Assert.Equal(PaymentStatus.Confirmed, Assert.Single(await PaymentsAsync()).Status);
        Assert.Equal(2, (await PaymentAuditAsync()).Count);
    }

    [Fact]
    public async Task Confirm_WhenNothingReported_ShowsNotice()
    {
        var response = await ConfirmFromDetailsAsync(_personal.Client, _periodId);

        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=sem-aviso", response.RedirectTarget());
        Assert.Contains("Este pagamento não está mais aguardando confirmação.", await PageAsync(_personal.Client, response.RedirectTarget()));
        await AssertUntouchedAsync();
    }

    // ---------------------------------------------------------------- Não recebi

    [Fact]
    public async Task Personal_Rejects_StudentSeesMessageAndReportsAgain()
    {
        await ReportByHandlerAsync();

        var page = await PageAsync(_personal.Client, $"/personal/financeiro/{_periodId}/nao-recebi");
        Assert.Contains("Você não recebeu os R$ 160,00 de Mariana?", page);
        Assert.Contains("O demonstrativo volta para \"Aguardando pagamento\" e Mariana pode informar de novo.", page);

        var response = await RejectAsync(_personal.Client, _periodId, "  Não caiu na conta  ");
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=nao-recebido", response.RedirectTarget());
        var details = await PageAsync(_personal.Client, response.RedirectTarget());
        Assert.Contains("O demonstrativo voltou para \"Aguardando pagamento\".", details);
        Assert.Contains("Você marcou que não recebeu", details);
        Assert.Contains("Registrar recebimento", details);

        var period = await PeriodAsync();
        Assert.Equal(BillingPeriodStatus.Issued, period.Status);
        var rejected = Assert.Single(await PaymentsAsync());
        Assert.Equal(PaymentStatus.Rejected, rejected.Status);
        Assert.Equal("Não caiu na conta", rejected.DecisionNote);

        var student = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}");
        Assert.Contains("O Carlos Lima ainda não recebeu este pagamento.", student);
        Assert.Contains("\"Não caiu na conta\"", student);
        Assert.Contains("Confira com ele e informe de novo quando estiver certo.", student);
        Assert.Contains("Informar pagamento de novo", student);
        Assert.Contains("Carlos Lima não recebeu", student);

        // Informa de novo: novo pagamento, o recusado fica no histórico.
        Assert.Equal($"/aluno/pagamentos/{_periodId}?aviso=informado", (await ReportAsync(_student.Client, _periodId)).RedirectTarget());
        var payments = await PaymentsAsync();
        Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Reported], payments.Select(p => p.Status));
        var again = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}");
        Assert.DoesNotContain("ainda não recebeu este pagamento", again);
        Assert.Contains("Você informou o pagamento em", again);

        var audit = await PaymentAuditAsync();
        // Relógio parado: as três ações têm o mesmo instante, então compara sem ordem.
        Assert.Equal(["PaymentRejected", "PaymentReported", "PaymentReported"], audit.Select(a => a.Action).Order());
        Assert.All(audit, a => Assert.DoesNotContain("caiu", a.Metadata));
    }

    [Fact]
    public async Task Reject_WithoutMessage_AndMessageTooLong()
    {
        await ReportByHandlerAsync();

        var tooLong = await RejectAsync(_personal.Client, _periodId, new string('x', Payment.TextMaxLength + 1));
        Assert.Equal(HttpStatusCode.OK, tooLong.StatusCode);
        Assert.Contains("A mensagem pode ter até 500 caracteres.", await tooLong.TextAsync());
        Assert.Equal(BillingPeriodStatus.PaymentReported, (await PeriodAsync()).Status);

        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=nao-recebido", (await RejectAsync(_personal.Client, _periodId, "   ")).RedirectTarget());
        Assert.Null(Assert.Single(await PaymentsAsync()).DecisionNote);
        Assert.DoesNotContain("ph-reason", (await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}"))
            .Split("ainda não recebeu")[1].Split("Confira com ele")[0]);
    }

    [Fact]
    public async Task Race_ConfirmAndRejectInParallel_EndConsistent()
    {
        for (var i = 0; i < 5; i++)
        {
            var periodId = await AddPeriodAsync(new DateOnly(2025, i + 1, 1));
            Assert.Equal(PaymentOutcome.Succeeded,
                await InScopeAsync(h => h.ReportAsync(_student.Account.Id, periodId, null, null, CancellationToken.None)));

            var confirm = InScopeAsync(h => h.ConfirmAsync(_personal.Account.Id, periodId, null, CancellationToken.None));
            var reject = InScopeAsync(h => h.RejectAsync(_personal.Account.Id, periodId, "Não recebi", null, CancellationToken.None));
            var results = await Task.WhenAll(confirm, reject);

            Assert.Single(results, r => r == PaymentOutcome.Succeeded);
            var period = await PeriodAsync(periodId);
            var payment = Assert.Single(await PaymentsAsync(periodId));
            if (results[0] == PaymentOutcome.Succeeded)
            {
                Assert.Equal(PaymentOutcome.AlreadyPaid, results[1]);
                Assert.Equal(BillingPeriodStatus.Paid, period.Status);
                Assert.Equal(PaymentStatus.Confirmed, payment.Status);
            }
            else
            {
                Assert.Equal(PaymentOutcome.NotReported, results[0]);
                Assert.Equal(BillingPeriodStatus.Issued, period.Status);
                Assert.Equal(PaymentStatus.Rejected, payment.Status);
            }
        }
    }

    // ---------------------------------------------------------------- Registro direto

    [Fact]
    public async Task Personal_RecordsPaymentWithoutStudentReport()
    {
        var details = await PageAsync(_personal.Client, $"/personal/financeiro/{_periodId}");
        Assert.Contains("Mariana ainda não informou o pagamento. Se pagou em dinheiro ou você já viu o Pix, registre o recebimento aqui.", details);
        Assert.Contains($"/personal/financeiro/{_periodId}/registrar", details);

        var page = await PageAsync(_personal.Client, $"/personal/financeiro/{_periodId}/registrar");
        Assert.Contains("Você recebeu os R$ 160,00 de Mariana?", page);

        var response = await RecordAsync(_personal.Client, _periodId);
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=registrado", response.RedirectTarget());
        var after = await PageAsync(_personal.Client, response.RedirectTarget());
        Assert.Contains("Recebimento registrado.", after);
        Assert.Contains("Recebimento registrado por você", after);

        var period = await PeriodAsync();
        Assert.Equal(BillingPeriodStatus.Paid, period.Status);
        Assert.Equal(Now.UtcDateTime, period.PaidAtUtc);
        var payment = Assert.Single(await PaymentsAsync());
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(160m, payment.Amount);
        Assert.Equal(_personal.Account.Id, payment.ReportedByUserId);
        Assert.Equal(_personal.Account.Id, payment.DecidedByUserId);
        Assert.Equal(["PaymentRecorded"], (await PaymentAuditAsync()).Select(a => a.Action));

        var student = await PageAsync(_student.Client, $"/aluno/pagamentos/{_periodId}");
        Assert.Contains("Pagamento confirmado pelo Carlos Lima em 12/11.", student);
        Assert.Contains("Recebimento registrado por Carlos Lima", student);
    }

    [Fact]
    public async Task Record_AfterStudentReported_AsksToConfirmInstead()
    {
        await ReportByHandlerAsync();

        Assert.Equal($"/personal/financeiro/{_periodId}",
            (await _personal.Client.GetAsync($"/personal/financeiro/{_periodId}/registrar")).RedirectTarget());
        var response = await RecordAsync(_personal.Client, _periodId);
        Assert.Equal($"/personal/financeiro/{_periodId}?aviso=ja-informado", response.RedirectTarget());
        Assert.Contains("O pagamento já foi informado pelo aluno.", await PageAsync(_personal.Client, response.RedirectTarget()));

        Assert.Equal(PaymentStatus.Reported, Assert.Single(await PaymentsAsync()).Status);
    }

    [Fact]
    public async Task Race_RecordAndReportInParallel_OnlyOneActivePayment()
    {
        for (var i = 0; i < 5; i++)
        {
            var periodId = await AddPeriodAsync(new DateOnly(2025, i + 1, 1));

            var record = InScopeAsync(h => h.RecordAsync(_personal.Account.Id, periodId, null, CancellationToken.None));
            var report = InScopeAsync(h => h.ReportAsync(_student.Account.Id, periodId, null, null, CancellationToken.None));
            var results = await Task.WhenAll(record, report);

            Assert.Single(results, r => r == PaymentOutcome.Succeeded);
            var payment = Assert.Single(await PaymentsAsync(periodId));
            Assert.Equal(results[0] == PaymentOutcome.Succeeded ? PaymentStatus.Confirmed : PaymentStatus.Reported, payment.Status);
        }
    }
}
