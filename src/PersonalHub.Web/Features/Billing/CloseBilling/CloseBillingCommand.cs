using System.Globalization;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.CloseBilling;

/// <summary>
/// <c>dotnet run --project src/PersonalHub.Web -- close-billing [--date aaaa-mm-dd]</c>
/// <para>
/// Roda o fechamento uma vez e encerra, sem subir o servidor web. <c>--date</c> simula o relógio (06:00 de São Paulo
/// nesse dia) e só é aceito em Development, para testar um vencimento sem esperar o dia chegar.
/// </para>
/// </summary>
public static class CloseBillingCommand
{
    public const string Name = "close-billing";

    public const string Usage = "Uso: dotnet run --project src/PersonalHub.Web -- close-billing [--date aaaa-mm-dd]";

    public static bool IsInvoked(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(string[] args)
    {
        DateOnly? date = null;
        var rest = args.Skip(1).ToArray();
        if (rest.Length == 2 && rest[0] == "--date"
            && DateOnly.TryParseExact(rest[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }
        else if (rest.Length != 0)
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        var builder = WebApplication.CreateBuilder();
        if (date is not null && !builder.Environment.IsDevelopment())
        {
            await Console.Error.WriteLineAsync("--date só pode ser usado em Development.");
            return 2;
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.MinLevel = LogLevel.Information;
            options.AddFilter("Microsoft", LogLevel.Warning);
        });
        builder.Services.AddPersistence();
        builder.Services.AddSingleton<TimeProvider>(date is { } day
            ? new FixedClock(SaoPaulo.ToUtc(day, new TimeOnly(6, 0))!.Value)
            : TimeProvider.System);
        builder.Services.AddScoped<BillingClosingJob>();

        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<BillingClosingJob>().RunAsync(CancellationToken.None);

        if (!result.Ran)
        {
            Console.WriteLine("Outra execução do fechamento está em andamento. Nada foi feito.");
            return 1;
        }

        Console.WriteLine(
            $"Fechamento concluído: {result.Due} vínculos devidos, {result.Issued} demonstrativos emitidos, " +
            $"{result.Empty} sem cobrança, {result.Failed} falhas.");
        return result.Failed == 0 ? 0 : 1;
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }
}
