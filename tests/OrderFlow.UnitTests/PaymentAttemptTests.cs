using System.Security.Cryptography;
using System.Text;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class PaymentAttemptTests
{
    private static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static PaymentAttempt Create()
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), "PAY", "Product", UsdMoney.FromDecimal(12.50m), 2)], Now);
        return PaymentAttempt.Create(order, Now);
    }

    [Theory]
    [InlineData(GatewayPaymentState.Pending, PaymentAttemptStatus.Pending, false)]
    [InlineData(GatewayPaymentState.Failed, PaymentAttemptStatus.Failed, false)]
    [InlineData(GatewayPaymentState.Succeeded, PaymentAttemptStatus.Pending, true)]
    public void Provider_observations_never_confirm_an_order_and_success_remains_unresolved(GatewayPaymentState state, PaymentAttemptStatus expected, bool attention)
    {
        var attempt = Create(); var key = attempt.ProviderRequestKey;
        attempt.Claim(Now);
        attempt.Observe(new(key, attempt.Id, attempt.OrderId, "txn_1", "25.00", "USD", state, state == GatewayPaymentState.Succeeded ? "cap_1" : null), null, Now);
        Assert.Equal(expected, attempt.Status); Assert.Equal(attention, attempt.NeedsAttention);
        Assert.Equal(state, attempt.ObservedState); Assert.Equal(key, attempt.ProviderRequestKey); Assert.Null(attempt.LeaseToken);
    }

    [Theory]
    [InlineData("24.99", "USD")]
    [InlineData("25.00", "EUR")]
    public void Mismatched_amount_or_currency_is_quarantined_and_cannot_free_the_active_slot(string amount, string currency)
    {
        var attempt = Create(); attempt.Claim(Now);
        attempt.Observe(new(attempt.ProviderRequestKey, attempt.Id, attempt.OrderId, "txn_1", amount, currency, GatewayPaymentState.Succeeded, "cap_1"), null, Now);
        Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status); Assert.True(attempt.NeedsAttention);
        Assert.Equal("gateway_response_mismatch", attempt.LastErrorCode); Assert.NotNull(attempt.LastObservation);
        Assert.Null(attempt.ProviderCaptureId);
    }

    [Fact]
    public void Timeout_stays_unknown_with_stable_identity_and_eight_deliveries_stop_automatic_retry()
    {
        var attempt = Create(); var key = attempt.ProviderRequestKey; var now = Now;
        for (var i = 1; i <= 8; i++)
        {
            attempt.Claim(now); attempt.Observe(null, "gateway_timeout", now);
            Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status); Assert.Equal(key, attempt.ProviderRequestKey);
            Assert.Equal(i, attempt.DeliveryCount); now = attempt.NextAttemptAt;
        }
        Assert.True(attempt.NeedsAttention); Assert.Throws<InvalidOperationException>(() => attempt.Claim(now));
    }

    [Fact]
    public void A_crash_after_the_eighth_claim_still_leaves_visible_exhaustion()
    {
        var attempt = Create(); var now = Now;
        for (var i = 0; i < 7; i++)
        {
            attempt.Claim(now); attempt.Observe(null, "gateway_timeout", now); now = attempt.NextAttemptAt;
        }
        attempt.Claim(now);
        Assert.True(attempt.NeedsAttention); Assert.Equal("reconciliation_exhausted", attempt.LastErrorCode);
        Assert.NotNull(attempt.LeaseToken); Assert.Equal(PaymentAttemptStatus.Unknown, attempt.Status);
    }

    [Fact]
    public void Active_lease_cannot_be_claimed_until_exact_deadline_and_reclaim_changes_token_only()
    {
        var attempt = Create(); var token = attempt.Claim(Now);
        Assert.Throws<InvalidOperationException>(() => attempt.Claim(Now.AddSeconds(59)));
        Assert.NotEqual(token, attempt.Claim(Now.AddSeconds(60))); Assert.Equal(2, attempt.DeliveryCount);
        Assert.Equal("25.00", attempt.Amount.ToString());
    }

    [Fact]
    public void Simulator_preserves_capture_identity_and_rejects_success_regression()
    {
        var request = new GatewayPaymentRequest("payment-test", Guid.NewGuid(), Guid.NewGuid(), "25.00", "USD");
        var payment = ProviderPayment.Create(request, UsdMoney.FromDecimal(25m), SimulatorScenario.Pending, Now);
        var transactionId = payment.TransactionId;
        payment.Resolve(GatewayPaymentState.Succeeded, Now.AddSeconds(1)); var captureId = payment.CaptureId; var eventId = payment.EventId;
        payment.Resolve(GatewayPaymentState.Succeeded, Now.AddSeconds(2));
        Assert.Equal(transactionId, payment.TransactionId); Assert.Equal(captureId, payment.CaptureId); Assert.Equal(eventId, payment.EventId);
        Assert.Throws<SimulatorConflictException>(() => payment.Resolve(GatewayPaymentState.Failed, Now));
    }

    [Fact]
    public void Callback_signature_covers_timestamp_and_exact_raw_bytes()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string body = "{\"amount\":\"25.00\"}";
        var signed = CallbackSigner.Sign("test-v1", secret, body, Now);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        Assert.Equal(Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed.Timestamp + "." + body))).ToLowerInvariant(), signed.Signature);
        Assert.NotEqual(signed.Signature, CallbackSigner.Sign("test-v1", secret, body + " ", Now).Signature);
        Assert.NotEqual(signed.Signature, CallbackSigner.Sign("test-v1", secret, body, Now.AddSeconds(1)).Signature);
    }
}
