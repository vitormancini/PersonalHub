using System.Globalization;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Billing;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;
using T = PersonalHub.Web.Domain.Notifications.NotificationTemplates;

namespace PersonalHub.Web.Features.Notifications;

/// <summary>
/// Os avisos de negócio da Fase 6, enfileirados pelos handlers dentro da própria transação (D-060 a D-062). Cada um
/// tem chave de idempotência derivada da entidade, um botão para a tela certa e nenhum texto livre de usuário: o aviso
/// só diz que há algo para ver.
/// </summary>
public static class BusinessNotifications
{
    /// <summary>Aula registrada → aluno.</summary>
    public static async Task LessonCreatedAsync(
        this AppDbContext db, Lesson lesson, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var c = await Contacts(db, lesson.PersonalStudentId, cancellationToken);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            T.LessonCreated, $"lesson-created:{lesson.Id}", c.StudentUserId, c.StudentPhone,
            new NotificationPayload(
                [c.StudentFirstName, T.Variable(c.PersonalName), Day(lesson.OccurredAtUtc), Time(lesson.OccurredAtUtc)],
                $"aluno/aulas/{lesson.Id}")), nowUtc, cancellationToken);
    }

    /// <summary>Aula confirmada ou recusada pelo aluno → personal. O motivo da recusa fica só no app.</summary>
    public static async Task LessonRespondedAsync(
        this AppDbContext db, Lesson lesson, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var c = await Contacts(db, lesson.PersonalStudentId, cancellationToken);
        var approved = lesson.Status == LessonStatus.Approved;
        await db.EnqueueNotificationAsync(new NotificationRequest(
            approved ? T.LessonApproved : T.LessonRejected,
            $"{(approved ? "lesson-approved" : "lesson-rejected")}:{lesson.Id}", c.PersonalUserId, c.PersonalPhone,
            new NotificationPayload(
                [T.Variable(c.StudentName), Day(lesson.OccurredAtUtc), Time(lesson.OccurredAtUtc)],
                approved ? "personal/aulas" : $"personal/aulas/{lesson.Id}")), nowUtc, cancellationToken);
    }

    /// <summary>
    /// Exclusão de aula já respondida → aluno (a pendente some para o aluno, D-042, e não gera aviso). O motivo fica
    /// só no app.
    /// </summary>
    public static async Task LessonDeletedAsync(
        this AppDbContext db, Lesson lesson, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (lesson.Status == LessonStatus.Pending)
        {
            return;
        }

        var c = await Contacts(db, lesson.PersonalStudentId, cancellationToken);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            T.LessonDeleted, $"lesson-deleted:{lesson.Id}", c.StudentUserId, c.StudentPhone,
            new NotificationPayload(
                [c.StudentFirstName, T.Variable(c.PersonalName), Day(lesson.OccurredAtUtc), Time(lesson.OccurredAtUtc)],
                "aluno/aulas")), nowUtc, cancellationToken);
    }

    /// <summary>
    /// Demonstrativo emitido pelo fechamento → aluno. Idempotente com o fechamento: a chave é o demonstrativo, que é
    /// único por vínculo e mês. Total zero já nasce pago e não gera aviso.
    /// </summary>
    public static async Task StatementIssuedAsync(
        this AppDbContext db, BillingPeriod period, int lessonCount, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (period.Status != BillingPeriodStatus.Issued)
        {
            return;
        }

        var c = await Contacts(db, period.PersonalStudentId, cancellationToken);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            T.StatementIssued, $"statement-issued:{period.Id}", c.StudentUserId, c.StudentPhone,
            new NotificationPayload(
                [
                    c.StudentFirstName, BillingDisplay.MonthNameLower(period.ReferenceMonth), T.Variable(c.PersonalName),
                    lessonCount.ToString(CultureInfo.InvariantCulture), BillingDisplay.Money(period.TotalAmount),
                    BillingDisplay.DayMonth(period.DueDate)
                ],
                $"aluno/pagamentos/{period.Id}")), nowUtc, cancellationToken);
    }

    /// <summary>Aluno informou o pagamento → personal. A observação do aluno fica só no app.</summary>
    public static async Task PaymentReportedAsync(
        this AppDbContext db, BillingPeriod period, Payment payment, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var c = await Contacts(db, period.PersonalStudentId, cancellationToken);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            T.PaymentReported, $"payment-reported:{payment.Id}", c.PersonalUserId, c.PersonalPhone,
            new NotificationPayload(
                [T.Variable(c.StudentName), BillingDisplay.MonthNameLower(period.ReferenceMonth), BillingDisplay.Money(payment.Amount)],
                $"personal/financeiro/{period.Id}")), nowUtc, cancellationToken);
    }

    /// <summary>
    /// Personal confirmou (ou registrou direto, D-052) ou recusou ("Não recebi", D-051) → aluno. A mensagem do personal
    /// fica só no app.
    /// </summary>
    public static async Task PaymentDecidedAsync(
        this AppDbContext db, BillingPeriod period, Payment payment, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var c = await Contacts(db, period.PersonalStudentId, cancellationToken);
        var confirmed = payment.Status == PaymentStatus.Confirmed;
        var month = BillingDisplay.MonthNameLower(period.ReferenceMonth);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            confirmed ? T.PaymentConfirmed : T.PaymentRejected,
            $"{(confirmed ? "payment-confirmed" : "payment-rejected")}:{payment.Id}", c.StudentUserId, c.StudentPhone,
            new NotificationPayload(
                confirmed
                    ? [c.StudentFirstName, T.Variable(c.PersonalName), month, BillingDisplay.Money(payment.Amount)]
                    : [c.StudentFirstName, T.Variable(c.PersonalName), month],
                $"aluno/pagamentos/{period.Id}")), nowUtc, cancellationToken);
    }

    private static async Task<LinkContacts> Contacts(AppDbContext db, Guid personalStudentId, CancellationToken cancellationToken) =>
        await db.LinkContactsAsync(personalStudentId, cancellationToken)
        ?? throw new InvalidOperationException($"Vínculo {personalStudentId} não encontrado para o aviso.");

    private static string Day(DateTime utc) => LessonDisplay.DayMonth(utc);

    private static string Time(DateTime utc) => LessonDisplay.Time(utc);
}
