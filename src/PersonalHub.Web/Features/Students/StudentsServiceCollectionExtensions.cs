using PersonalHub.Web.Features.Students.AcceptInvite;
using PersonalHub.Web.Features.Students.ChangeLinkStatus;
using PersonalHub.Web.Features.Students.CreateStudent;
using PersonalHub.Web.Features.Students.EditStudent;
using PersonalHub.Web.Features.Students.ListStudents;
using PersonalHub.Web.Features.Students.ManageInvite;
using PersonalHub.Web.Features.Students.StudentDetails;

namespace PersonalHub.Web.Features.Students;

public static class StudentsServiceCollectionExtensions
{
    public static IServiceCollection AddStudents(this IServiceCollection services)
    {
        services.AddScoped<CreateStudentHandler>();
        services.AddScoped<InviteCreatedQuery>();
        services.AddSingleton<CreatedInviteHandoff>();
        services.AddScoped<AcceptInviteHandler>();
        services.AddSingleton<InvitePhoneProof>();
        services.AddInviteRateLimiting();

        services.AddScoped<ListStudentsQuery>();
        services.AddScoped<StudentDetailsQuery>();
        services.AddScoped<EditStudentQuery>();
        services.AddScoped<EditStudentHandler>();
        services.AddScoped<InviteActionsHandler>();
        services.AddScoped<ChangeLinkStatusHandler>();

        return services;
    }
}
