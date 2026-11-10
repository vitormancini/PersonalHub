using PersonalHub.Web.Features.Lessons.DeleteLesson;
using PersonalHub.Web.Features.Lessons.LessonDetails;
using PersonalHub.Web.Features.Lessons.ListLessons;
using PersonalHub.Web.Features.Lessons.RegisterLesson;
using PersonalHub.Web.Features.Lessons.RespondLesson;
using PersonalHub.Web.Features.Lessons.StudentLessons;

namespace PersonalHub.Web.Features.Lessons;

public static class LessonsServiceCollectionExtensions
{
    public static IServiceCollection AddLessons(this IServiceCollection services)
    {
        services.AddScoped<RegisterLessonQuery>();
        services.AddScoped<RegisterLessonHandler>();
        services.AddScoped<ListLessonsQuery>();
        services.AddScoped<LessonDetailsQuery>();
        services.AddScoped<DeleteLessonHandler>();
        services.AddScoped<StudentLessonsQuery>();
        services.AddScoped<RespondLessonHandler>();

        return services;
    }
}
