using System.Text;
using OrderFlow.Application.Persistence;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class IdempotencyRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completion_copies_the_response_and_cannot_replace_the_original()
    {
        var record = Create();
        var response = Encoding.UTF8.GetBytes("{\"original\":true}");
        record.Complete(201, "application/json", response, "/resource", Now);
        response[0] = (byte)'x';

        Assert.Equal("{\"original\":true}", Encoding.UTF8.GetString(record.ResponseBody!));
        Assert.Equal(IdempotencyStatus.Completed, record.Status);
        Assert.Equal(Now, record.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => record.Complete(200, "application/json", [], null, Now));
        Assert.Equal(201, record.ResponseStatusCode);
    }

    [Theory]
    [InlineData(199)]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(500)]
    public void Only_successful_responses_can_complete_the_record(int status)
    {
        var record = Create();
        Assert.Throws<InvalidOperationException>(() => record.Complete(status, "application/json", [], null, Now));
        Assert.Equal(IdempotencyStatus.InProgress, record.Status);
        Assert.Null(record.ResponseBody);
    }

    [Fact]
    public void Completion_before_creation_does_not_change_the_claim()
    {
        var record = Create();
        Assert.Throws<InvalidOperationException>(() => record.Complete(201, "application/json", [], null, Now.AddSeconds(-1)));
        Assert.Equal(IdempotencyStatus.InProgress, record.Status);
        Assert.Null(record.CompletedAt);
    }

    private static IdempotencyRecord Create() => IdempotencyRecord.Create(Guid.NewGuid(), "Test:v1", "key", "{}", Now);
}
