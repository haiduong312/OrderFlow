using System.Net;
using OrderFlow.IntegrationTests.Http;
using Xunit;

namespace OrderFlow.IntegrationTests.Frontend;

public sealed class BrowserControlAbsenceTests
{
    [Fact]
    public async Task Normal_API_does_not_expose_browser_clock_control()
    {
        await using var factory = new FoundationApiFactory(); using var client = factory.CreateClient();
        using var get = await client.GetAsync("/api/_browser/advance-clock");
        using var post = await client.PostAsync("/api/_browser/advance-clock", null);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode); Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        using var checkoutGet = await client.GetAsync("/api/_checkout-ui/submit");
        using var checkoutPost = await client.PostAsync("/api/_checkout-ui/submit", null);
        Assert.Equal(HttpStatusCode.NotFound, checkoutGet.StatusCode); Assert.Equal(HttpStatusCode.NotFound, checkoutPost.StatusCode);
    }
}
