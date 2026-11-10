using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Billing.Payments;
using PersonalHub.Web.Features.Billing.PersonalBilling;
using PersonalHub.Web.Features.Billing.Statement;
using PersonalHub.Web.Features.Billing.StudentPayments;

namespace PersonalHub.Web.Features.Billing;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingClosingOptions>(configuration.GetSection(BillingClosingOptions.Section));
        services.AddScoped<BillingClosingJob>();
        services.AddHostedService<BillingClosingWorker>();

        services.AddScoped<PersonalBillingQuery>();
        services.AddScoped<StatementQuery>();
        services.AddScoped<StudentPaymentsQuery>();
        services.AddScoped<PaymentHandler>();

        return services;
    }
}
