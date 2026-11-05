using System.Globalization;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Students;

namespace PersonalHub.Web.Features.Students;

/// <summary>Formatos brasileiros usados nas telas de alunos.</summary>
public static class StudentDisplay
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"R$ 1.234,50", com espaço comum (o "C" do .NET usa espaço não separável).</summary>
    public static string Money(decimal value) => "R$ " + value.ToString("N2", PtBr);

    public static string Date(DateTime utc) => SaoPaulo.ToLocalDate(utc).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DayMonth(DateTime utc) => SaoPaulo.ToLocalDate(utc).ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>Iniciais do primeiro e do último nome: "Mariana Lopes" vira "ML".</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        var first = char.ToUpper(parts[0][0], PtBr);
        return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpper(parts[^1][0], PtBr)}";
    }

    public static string StatusLabel(PersonalStudentStatus status) => status switch
    {
        PersonalStudentStatus.Active => "Ativo",
        PersonalStudentStatus.Invited => "Convidado",
        _ => "Inativo"
    };

    public static string StatusPillClass(PersonalStudentStatus status) => status switch
    {
        PersonalStudentStatus.Active => "ph-pill-success",
        PersonalStudentStatus.Invited => "ph-pill-pending",
        _ => "ph-pill-muted"
    };
}
