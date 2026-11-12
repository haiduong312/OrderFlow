using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Payments;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class RefundDomainTests
{
    private static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static PaymentRefund Create() => PaymentRefund.Create(new()
    {
        Id = Guid.NewGuid(), AttemptId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
        ProviderCaptureId = "capture", ProviderTransactionId = "transaction", Amount = UsdMoney.FromDecimal(25m)
    }, "late_capture", Now);

    [Fact]
    public void Retry_keeps_capture_and_provider_key_backoff_is_bounded_and_exhaustion_is_durable_at_claim()
    {
        var refund = Create(); var key = refund.ProviderRefundKey; var time = Now;
        int[] delays = [5,15,30,60,120,300,600];
        for (var i = 1; i <= 8; i++)
        {
            refund.Claim(time); Assert.Equal(key, refund.ProviderRefundKey); Assert.Equal(i, refund.DeliveryCount);
            if (i == 8)
            {
                Assert.Equal(PaymentRefundStatus.NeedsReview, refund.Status);
                Assert.Equal("refund_retries_exhausted", refund.LastErrorCode); // crash at last claim is visible
            }
            refund.Observe(null, "gateway_timeout", true, time);
            if (i < 8) { Assert.Equal(time.AddSeconds(delays[i-1]), refund.NextAttemptAt); time = refund.NextAttemptAt; }
        }
        Assert.Throws<InvalidOperationException>(() => refund.Claim(refund.NextAttemptAt));
        Assert.Equal("capture", refund.ProviderCaptureId); Assert.Equal("25.00", refund.Amount.ToString());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("capture")]
    [InlineData("key")]
    [InlineData("currency")]
    public void Refund_response_must_match_every_immutable_identity_and_money_field(string changed)
    {
        var refund = Create(); refund.Claim(Now);
        var result = new GatewayRefundResult(refund.ProviderRefundKey,"provider-refund","capture","25.00","USD");
        result = changed switch
        {
            "amount" => result with { Amount="24.99" }, "capture" => result with { CaptureId="other" },
            "key" => result with { RequestKey="other" }, _ => result with { Currency="EUR" }
        };
        refund.Observe(result, null, true, Now);
        Assert.Equal(PaymentRefundStatus.NeedsReview, refund.Status); Assert.Null(refund.ProviderRefundId);
        Assert.Equal("refund_response_mismatch", refund.LastErrorCode);
    }

    [Fact]
    public void Success_is_monotonic_and_permanent_failure_goes_to_review_without_retry()
    {
        var refund = Create(); refund.Claim(Now);
        refund.Observe(new(refund.ProviderRefundKey,"provider-refund","capture","25.00","USD"),null,true,Now);
        refund.Observe(null,"gateway_timeout",true,Now.AddSeconds(1));
        Assert.Equal(PaymentRefundStatus.Succeeded, refund.Status); Assert.Equal("provider-refund", refund.ProviderRefundId);
        Assert.Null(refund.LeaseToken); Assert.Throws<InvalidOperationException>(() => refund.Claim(Now.AddMinutes(1)));
        var rejected = Create(); rejected.Claim(Now); rejected.Observe(null,"gateway_http_error",false,Now);
        Assert.Equal(PaymentRefundStatus.NeedsReview, rejected.Status);
        Assert.Throws<InvalidOperationException>(() => rejected.Claim(Now.AddHours(1)));
    }
}
