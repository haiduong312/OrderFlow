using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Emails;

public sealed class EmailSendingIteration(IServiceScopeFactory scopes, IEmailGateway gateway, TimeProvider clock,
    ILogger<EmailSendingIteration> logger)
{
    private readonly string owner = "email-" + Guid.NewGuid().ToString("N");
    public async Task<bool> ProcessNextAsync(CancellationToken token)
    {
        EmailDelivery claimed; Guid lease; bool previouslyUncertain;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await OrderDatabase.SetTimeoutsAsync(db, token); var now = BusinessTime.GetUtcNow(clock);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH exhausted AS (
                    SELECT "Id" FROM notifications."EmailDeliveries" WHERE "AttemptsInCycle">=8
                        AND ("Status"='Pending' OR ("Status"='InFlight' AND "LeaseExpiresAt"<={now}))
                    ORDER BY "NextAttemptAt","Id" LIMIT 20 FOR UPDATE SKIP LOCKED
                )
                UPDATE notifications."EmailDeliveries" d SET "Status"='NeedsReview',"UpdatedAt"={now},
                    "LastFailureAt"={now},"LastErrorCode"='email_attempts_exhausted',"LeaseOwner"=NULL,"LeaseToken"=NULL,"LeaseExpiresAt"=NULL
                FROM exhausted e WHERE d."Id"=e."Id"
                """, token);
            var rows = await db.EmailDeliveries.FromSqlInterpolated($"""
                SELECT * FROM notifications."EmailDeliveries" WHERE "AttemptsInCycle"<8 AND (
                    ("Status"='Pending' AND "NextAttemptAt"<={now}) OR ("Status"='InFlight' AND "LeaseExpiresAt"<={now}))
                ORDER BY "NextAttemptAt","Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(token);
            if (rows.Count == 0) { await transaction.CommitAsync(token); return false; }
            claimed = rows[0]; previouslyUncertain = claimed.OutcomeUncertain;
            lease = Guid.NewGuid(); claimed.LeaseToken = lease; claimed.LeaseOwner = owner; claimed.LeaseExpiresAt = now.AddSeconds(60);
            claimed.Status = EmailDeliveryStatus.InFlight; claimed.AttemptCount++; claimed.AttemptsInCycle++; claimed.UpdatedAt = now;
            // Persist before any possible send: shutdown/crash must leave uncertainty visible even if no result can be saved.
            claimed.OutcomeUncertain = true;
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }

        EmailGatewayResult? result = null; EmailGatewayException? failure = null;
        try
        {
            if (claimed.Provider != gateway.Name) throw new EmailGatewayException("email_provider_mismatch", false, previouslyUncertain);
            var request = claimed.Request();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(20));
            if (gateway.SupportsLookup)
            {
                result = await gateway.LookupAsync(request, budget.Token);
                if (!Matches(result, request)) throw new EmailGatewayException("email_response_mismatch", false, true);
                if (result.State == EmailProviderState.Absent)
                {
                    if (previouslyUncertain && !gateway.SupportsIdempotentSend)
                        throw new EmailGatewayException("email_absent_after_uncertain_send", false, true);
                    previouslyUncertain = false; result = null;
                }
            }
            // A safe provider uses the SAME key/body; SMTP explicitly cannot deduplicate this retry.
            result ??= await gateway.SendAsync(request, budget.Token);
            if (!Matches(result, request) || result.State == EmailProviderState.Absent)
                throw new EmailGatewayException("email_response_mismatch", false, true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { failure = new EmailGatewayException("email_gateway_timeout", true, true); }
        catch (EmailGatewayException error)
        {
            failure = error;
            logger.LogWarning(new EventId(2001, "EmailGatewayFailure"), "Email delivery {DeliveryId} attempt {Attempt} failed {Code}; uncertain {Uncertain}",
                claimed.Id, claimed.AttemptCount, error.Code, previouslyUncertain || error.Uncertain);
        }
        var sent = failure is null && result?.State == EmailProviderState.Sent;
        var retryable = failure?.Retryable ?? result?.State == EmailProviderState.Pending;
        var uncertain = !sent && (previouslyUncertain || failure?.Uncertain == true || result?.State == EmailProviderState.Pending);
        var errorCode = failure?.Code ?? (result?.State == EmailProviderState.Rejected ? "email_provider_rejected" : "email_provider_pending");
        var status = sent ? "Sent" : retryable && claimed.AttemptsInCycle < 8 ? "Pending" : "NeedsReview";
        if (!sent && claimed.AttemptsInCycle >= 8) errorCode = "email_attempts_exhausted";
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(); var now = BusinessTime.GetUtcNow(clock);
            int[] seconds = [5, 15, 30, 60, 120, 300, 600, 600];
            var next = now.AddSeconds(seconds[Math.Clamp(claimed.AttemptsInCycle - 1, 0, 7)]);
            var code = sent ? null : errorCode; DateTimeOffset? sentAt = sent ? now : null;
            DateTimeOffset? failedAt = sent ? claimed.LastFailureAt : now;
            var providerId = sent ? result!.ProviderMessageId : claimed.ProviderMessageId;
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE notifications."EmailDeliveries" SET "Status"={status},"SentAt"={sentAt},"UpdatedAt"={now},"NextAttemptAt"={next},
                    "ProviderMessageId"={providerId},"OutcomeUncertain"={uncertain},"LastErrorCode"={code},"LastFailureAt"={failedAt},
                    "LeaseOwner"=NULL,"LeaseToken"=NULL,"LeaseExpiresAt"=NULL
                WHERE "Id"={claimed.Id} AND "Status"='InFlight' AND "LeaseOwner"={owner} AND "LeaseToken"={lease} AND "LeaseExpiresAt">{now}
                """, token);
            if (changed == 0) logger.LogWarning(new EventId(2002, "EmailLeaseLost"), "Email delivery {DeliveryId} result ignored after lease changed/expired", claimed.Id);
        }
        return true;
    }
    private static bool Matches(EmailGatewayResult result, EmailRequest request) => result.ProviderKey == request.ProviderKey
        && result.RequestHash == request.RequestHash && Enum.IsDefined(result.State)
        && (result.ProviderMessageId is null || result.ProviderMessageId.Length is > 0 and <= 200);
}
