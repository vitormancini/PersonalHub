namespace PersonalHub.Web.Features.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationWorkerOptions>(configuration.GetSection(NotificationWorkerOptions.Section));
        services.AddSingleton<OutboxCodeProtector>();
        services.AddScoped<NotificationDispatcher>();
        services.AddHostedService<NotificationWorker>();
        return services;
    }
}
