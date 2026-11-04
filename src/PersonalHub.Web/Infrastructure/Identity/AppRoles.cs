namespace PersonalHub.Web.Infrastructure.Identity;

public static class AppRoles
{
    public const string Personal = "Personal";
    public const string Student = "Student";

    // Ids fixos para o seed das roles ficar estável entre migrations.
    public static readonly Guid PersonalId = new("6f1c2a52-3b7e-4d5a-9c1e-2f8a4b7d1001");
    public static readonly Guid StudentId = new("6f1c2a52-3b7e-4d5a-9c1e-2f8a4b7d1002");
}
