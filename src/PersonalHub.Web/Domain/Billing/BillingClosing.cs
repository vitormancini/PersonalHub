using System.Globalization;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.Web.Domain.Billing;

/// <summary>
/// Aula candidata ao fechamento. <paramref name="BilledAmount"/> é o valor do item <c>Lesson</c> quando a aula já foi
/// cobrada em algum demonstrativo; <paramref name="Credited"/> indica que ela já virou crédito.
/// </summary>
public sealed record ClosingLesson(
    Guid Id,
    DateTime OccurredAtUtc,
    decimal UnitPrice,
    LessonStatus Status,
    DateTime? RespondedAtUtc,
    DateTime? DeletedAtUtc,
    decimal? BilledAmount = null,
    bool Credited = false);

/// <summary>Saldo de crédito que sobrou do último demonstrativo (D-044).</summary>
public sealed record CarriedCredit(DateOnly FromReferenceMonth, decimal Amount);

public sealed record ClosingItem(BillingPeriodItemKind Kind, Guid? LessonId, string Description, decimal Amount);

/// <summary>Resultado do fechamento de um mês de referência de um vínculo.</summary>
public sealed record ClosingResult(
    DateOnly ReferenceMonth,
    DateOnly DueDate,
    IReadOnlyList<ClosingItem> Items,
    decimal TotalAmount,
    decimal CarryOut)
{
    public int LessonCount => Items.Count(i => i.Kind == BillingPeriodItemKind.Lesson);

    public bool IsZero => TotalAmount == 0m;
}

/// <summary>
/// Regras do fechamento mensal (D-019, D-020, D-023, D-025, D-043 a D-046). Funções puras: o job só busca as aulas e
/// grava o resultado.
/// <para>
/// O corte é a meia-noite (America/Sao_Paulo) do dia do vencimento: entra o que estava confirmado antes dele, e uma
/// aula excluída antes dele vira crédito. Assim o resultado não depende da hora em que o job roda (inclusive quando
/// recupera dias perdidos com o app fora do ar).
/// </para>
/// </summary>
public static class BillingClosing
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Dia em que o mês de referência fecha: o vencimento do mês seguinte, ajustado por D-025.</summary>
    public static DateOnly ClosingDate(DateOnly referenceMonth, int billingDueDay)
    {
        var next = FirstOfMonth(referenceMonth).AddMonths(1);
        return BillingCalendar.DueDateIn(next.Year, next.Month, billingDueDay);
    }

    /// <summary>Instante do corte (00:00 de São Paulo no dia do vencimento), em UTC.</summary>
    public static DateTime CutoffUtc(DateOnly dueDate) => SaoPaulo.StartOfDayUtc(dueDate);

    /// <summary>
    /// Mês de referência mais recente cujo fechamento já chegou em <paramref name="today"/> (data de São Paulo).
    /// </summary>
    public static DateOnly LatestDueReferenceMonth(DateOnly today, int billingDueDay)
    {
        var current = FirstOfMonth(today);
        return today >= BillingCalendar.DueDateIn(today.Year, today.Month, billingDueDay)
            ? current.AddMonths(-1)
            : current.AddMonths(-2);
    }

    /// <summary>
    /// Mês a fechar agora, ou nulo. Só fecha o mês mais recente já vencido e nunca volta atrás de um demonstrativo
    /// existente: se o app ficou fora por meses, as aulas antigas entram nesse fechamento (D-043, D-047).
    /// </summary>
    public static DateOnly? DueReferenceMonth(DateOnly today, int billingDueDay, DateOnly? latestIssuedReferenceMonth)
    {
        var due = LatestDueReferenceMonth(today, billingDueDay);
        return latestIssuedReferenceMonth is { } latest && latest >= due ? null : due;
    }

    /// <summary>Próximo fechamento que ainda vai acontecer (depois de hoje): mês de referência e dia.</summary>
    public static (DateOnly ReferenceMonth, DateOnly ClosingDate) NextClosing(DateOnly today, int billingDueDay)
    {
        var reference = LatestDueReferenceMonth(today, billingDueDay).AddMonths(1);
        return (reference, ClosingDate(reference, billingDueDay));
    }

    /// <summary>
    /// Monta o demonstrativo do mês de referência. Nulo quando não há aula a cobrar nem crédito novo (D-044): o saldo
    /// levado adiante, se houver, espera o próximo demonstrativo.
    /// </summary>
    public static ClosingResult? Compose(
        DateOnly referenceMonth, int billingDueDay, IEnumerable<ClosingLesson> lessons, CarriedCredit? carried)
    {
        referenceMonth = FirstOfMonth(referenceMonth);
        var dueDate = ClosingDate(referenceMonth, billingDueDay);
        return ComposeAt(referenceMonth, dueDate, CutoffUtc(dueDate), lessons, carried);
    }

    /// <summary>
    /// Prévia do que entraria se o mês fechasse agora (<paramref name="nowUtc"/> no lugar do corte). Usada nas telas
    /// "Ainda vão fechar" e "até agora".
    /// </summary>
    public static ClosingResult? Preview(
        DateOnly referenceMonth, int billingDueDay, IEnumerable<ClosingLesson> lessons, CarriedCredit? carried,
        DateTime nowUtc)
    {
        referenceMonth = FirstOfMonth(referenceMonth);
        var dueDate = ClosingDate(referenceMonth, billingDueDay);
        var cutoff = CutoffUtc(dueDate);
        return ComposeAt(referenceMonth, dueDate, nowUtc < cutoff ? nowUtc.AddTicks(1) : cutoff, lessons, carried);
    }

    /// <summary>A aula entra como cobrança: confirmada antes do corte, não excluída, ainda não cobrada e do mês ou antes.</summary>
    public static bool IsChargeable(ClosingLesson lesson, DateTime monthEndUtc, DateTime cutoffUtc) =>
        lesson is { Status: LessonStatus.Approved, DeletedAtUtc: null, BilledAmount: null }
        && lesson.OccurredAtUtc < monthEndUtc
        && lesson.RespondedAtUtc is { } responded && responded < cutoffUtc;

    /// <summary>A aula vira crédito: já cobrada, excluída antes do corte e ainda não creditada (D-023, D-045).</summary>
    public static bool IsCreditable(ClosingLesson lesson, DateTime cutoffUtc) =>
        lesson is { BilledAmount: not null, Credited: false }
        && lesson.DeletedAtUtc is { } deleted && deleted < cutoffUtc;

    public static string LessonDescription(DateTime occurredAtUtc) => $"Aula de {DayMonthTime(occurredAtUtc)}";

    public static string CreditDescription(DateTime occurredAtUtc) => $"Crédito da aula de {DayMonthTime(occurredAtUtc)}";

    public static string CarryDescription(DateOnly fromReferenceMonth) =>
        $"Saldo de crédito de {fromReferenceMonth.ToString("MMMM 'de' yyyy", PtBr)}";

    /// <summary>Saldo que sobra de um demonstrativo cujos itens somam <paramref name="itemsSum"/>.</summary>
    public static decimal CarryOutOf(decimal itemsSum) => itemsSum < 0 ? -itemsSum : 0m;

    private static ClosingResult? ComposeAt(
        DateOnly referenceMonth, DateOnly dueDate, DateTime cutoffUtc, IEnumerable<ClosingLesson> lessons,
        CarriedCredit? carried)
    {
        var (_, monthEndUtc) = SaoPaulo.MonthRangeUtc(referenceMonth.Year, referenceMonth.Month);
        var all = lessons.ToList();

        var charges = all
            .Where(l => IsChargeable(l, monthEndUtc, cutoffUtc))
            .OrderBy(l => l.OccurredAtUtc).ThenBy(l => l.Id)
            .Select(l => new ClosingItem(BillingPeriodItemKind.Lesson, l.Id, LessonDescription(l.OccurredAtUtc), l.UnitPrice));

        var credits = all
            .Where(l => IsCreditable(l, cutoffUtc))
            .OrderBy(l => l.OccurredAtUtc).ThenBy(l => l.Id)
            .Select(l => new ClosingItem(BillingPeriodItemKind.Adjustment, l.Id, CreditDescription(l.OccurredAtUtc), -l.BilledAmount!.Value));

        var items = charges.Concat(credits).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        if (carried is { Amount: > 0 })
        {
            items.Add(new ClosingItem(BillingPeriodItemKind.Adjustment, null, CarryDescription(carried.FromReferenceMonth), -carried.Amount));
        }

        var sum = items.Sum(i => i.Amount);
        return new ClosingResult(referenceMonth, dueDate, items, Math.Max(0m, sum), CarryOutOf(sum));
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    private static string DayMonthTime(DateTime utc) =>
        SaoPaulo.ToLocal(utc).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);
}
