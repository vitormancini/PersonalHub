using PersonalHub.Web.Features.Students.AcceptInvite;
using PersonalHub.Web.Features.Students.CreateStudent;

namespace PersonalHub.Web.Features.Students;

public static class StudentsServiceCollectionExtensions
{
    public static IServiceCollection AddStudents(this IServiceCollection services)
    {
        services.AddScoped<CreateStudentHandler>();
        services.AddScoped<InviteCreatedQuery>();
        services.AddSingleton<CreatedInviteHandoff>();
        services.AddScoped<AcceptInviteHandler>();
        services.AddInviteRateLimiting();

        return services;
    }
}
