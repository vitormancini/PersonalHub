using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Features.Billing.CloseBilling;

public sealed class BillingClosingOptions
{
    public const string Section = "Billing:Closing";

    /// <summary>Desligado nos testes, que chamam o job diretamente.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Agendamento do fechamento (D-047): roda na partida e a cada hora dentro do próprio app. Como o job olha o estado do
/// banco (e não "o que mudou desde a última vez"), uma execução perdida é recuperada na próxima, e reiniciar o app não
/// emite nada em dobro.
/// </summary>
public sealed partial class BillingClosingWorker(
    IServiceScopeFactory scopes, IOptions<BillingClosingOptions> options, TimeProvider clock,
    ILogger<BillingClosingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(settings.Interval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BillingClosingJob>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Banco fora do ar, por exemplo: tenta de novo no próximo ciclo.
                LogFailed(logger, ex);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information, Message = "Fechamento automático desligado por configuração")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Error, Message = "Execução do fechamento falhou; nova tentativa no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
