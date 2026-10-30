using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using Xunit;

namespace OrderFlow.UnitTests;

public class PageRequestTests
{
    [Fact]
    public void Defaults_and_last_valid_offset_are_explicit()
    {
        Assert.Equal(1, PageRequest.Create().Page);
        Assert.Equal(20, PageRequest.Create().PageSize);
        Assert.Equal(0, PageRequest.Create().Offset);
        Assert.Equal(100, PageRequest.Create(2, 100).Offset);
        Assert.Equal(int.MaxValue - 1, PageRequest.Create(int.MaxValue, 1).Offset);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    [InlineData(int.MaxValue, 100, "page")]
    public void Invalid_values_and_overflow_are_validation_failures(int page, int size, string field)
    {
        var failure = Assert.Throws<ApplicationFailureException>(() => PageRequest.Create(page, size));
        Assert.Equal(ApplicationError.ValidationFailed, failure.Error);
        Assert.Contains(field, failure.Errors.Keys);
    }
}
