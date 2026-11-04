namespace PersonalHub.Web.Features.Students.CreateStudent;

/// <summary>Campos do formulário "Novo aluno", como texto. O handler valida e converte tudo no servidor.</summary>
public sealed class CreateStudentInput
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? LessonPrice { get; set; }
    public string? BillingDueDay { get; set; }
}
