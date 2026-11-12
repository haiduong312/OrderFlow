using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Application.Modules.Ordering;
using Xunit;

namespace OrderFlow.UnitTests;
public sealed class EmailDeliveryTests
{
    private static Order Order() => OrderFlow.Application.Modules.Ordering.Order.Create(Guid.NewGuid(), Guid.NewGuid(),
        [new(Guid.NewGuid(), "SKU", "Test product", UsdMoney.FromDecimal(12.50m), 1)], DateTimeOffset.UnixEpoch);
    [Fact]
    public void Stable_identity_and_snapshot_bind_recipient_template_money_and_body()
    {
        var order = Order(); var source = Guid.NewGuid(); var d = EmailDelivery.Confirmation(order, source, "owner@example.invalid", DateTimeOffset.UnixEpoch);
        var request = d.Request(); d.AttemptCount++; d.ReviewRetryCount++; d.LeaseToken = Guid.NewGuid();
        Assert.Equal(request, d.Request()); Assert.Equal(source, d.SourceEventId); Assert.Equal("order-confirmation:v1", d.Template);
        Assert.Contains("12.50 USD", request.TextBody, StringComparison.Ordinal); Assert.Equal(32, d.RequestHash.Length);
        Assert.NotEqual(d.ProviderKey, EmailDelivery.Confirmation(order, source, "other@example.invalid", DateTimeOffset.UnixEpoch).ProviderKey);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("bad-address")] [InlineData("owner@example.invalid\r\nBcc: injected@example.invalid")]
    public void Invalid_recipient_creates_reviewable_intention_instead_of_failing_order_consumer(string? recipient)
    {
        var d = EmailDelivery.Confirmation(Order(), Guid.NewGuid(), recipient, DateTimeOffset.UnixEpoch);
        Assert.Equal(EmailDeliveryStatus.NeedsReview, d.Status); Assert.Equal("email_recipient_invalid", d.LastErrorCode);
    }
}
