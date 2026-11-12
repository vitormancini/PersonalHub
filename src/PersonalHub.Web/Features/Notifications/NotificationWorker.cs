using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Features.Notifications;

/// <summary>
/// Envia a outbox a cada ~10 s dentro do próprio app (como o fechamento, D-047). Várias instâncias podem rodar juntas:
/// o <c>SKIP LOCKED</c> do dispatcher divide as linhas entre elas.
/// </summary>
public sealed partial class NotificationWorker(
    IServiceScopeFactory scopes, IOptions<NotificationWorkerOptions> options, TimeProvider clock,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(settings.PollInterval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                // Lote cheio: provavelmente há mais na fila; segue sem esperar o próximo ciclo.
                while ((await dispatcher.RunOnceAsync(stoppingToken)).Claimed >= settings.BatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
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

    [LoggerMessage(EventId = 7010, Level = LogLevel.Information, Message = "Worker de notificações desligado por configuração")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Error, Message = "Rodada do worker de notificações falhou; nova tentativa no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
