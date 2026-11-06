using Microsoft.Extensions.Options;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Worker;

public sealed record OrderExpiryResult(int Expired, int Contended);

/// <summary>Callable bounded iteration. Each attempt gets its own scope/context/transaction.</summary>
public sealed class OrderExpiryIteration(IServiceScopeFactory scopes, IOptions<OrderExpiryOptions> options,
    ILogger<OrderExpiryIteration> logger)
{
    public async Task<OrderExpiryResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var expired = 0;
        var contended = 0;
        for (var attempt = 0; attempt < options.Value.BatchSize; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                if (!await scope.ServiceProvider.GetRequiredService<OrderExpiryProcessor>().ProcessNextAsync(cancellationToken)) break;
                expired++;
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
            {
                contended++;
                logger.LogWarning(new EventId(1401, "OrderExpiryContention"),
                    "Expiry attempt rolled back with PostgreSQL state {SqlState}; remaining work stays due",
                    OrderDatabase.Contention(failure)!.SqlState);
                // Do not hot-loop on the same blocked row. The scheduler delays the next iteration.
                break;
            }
        }
        return new(expired, contended);
    }
}
