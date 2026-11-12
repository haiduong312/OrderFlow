using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Worker.Emails;

public sealed class EmailSendingWorker(EmailSendingIteration iteration, TimeProvider clock, ILogger<EmailSendingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    for (var i = 0; i < 20; i++) if (!await iteration.ProcessNextAsync(stoppingToken)) break;
                }
                catch (Exception failure) when (!stoppingToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
                { logger.LogWarning(new EventId(2003, "EmailDatabaseContention"), "Email pass postponed by PostgreSQL contention"); }
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
