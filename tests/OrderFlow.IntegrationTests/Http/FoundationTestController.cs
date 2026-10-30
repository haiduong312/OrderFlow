using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Pagination;

namespace OrderFlow.IntegrationTests.Http;

// Registered explicitly by FoundationApiFactory, not a production application part.
[ApiController]
[Route("_tests/foundations")]
public sealed class FoundationTestController : ControllerBase
{
    [HttpPost("echo")]
    public ActionResult<EchoRequest> Echo(EchoRequest request) => Ok(request);

    [HttpGet("business/{error}")]
    public IActionResult Business(ApplicationError error) => throw new ApplicationFailureException(error,
        error == ApplicationError.ValidationFailed
            ? new Dictionary<string, string[]> { ["quantity"] = ["Quantity must be positive."] }
            : null);

    [HttpGet("status/{status:int}")]
    public IActionResult BareStatus(int status) => StatusCode(status);

    [HttpGet("unexpected/{kind}")]
    public IActionResult Unexpected(string kind) => throw (kind switch
    {
        "database" => new SyntheticDatabaseException(),
        "cancellation" => new OperationCanceledException("sensitive-test-marker"),
        _ => new InvalidOperationException("sensitive-test-marker")
    });

    [HttpGet("page")]
    public PageRequest Page([FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PageRequest.Create(page, pageSize);

    [HttpGet("cancel")]
    public async Task<IActionResult> Cancel([FromServices] CancellationProbe probe, CancellationToken cancellationToken)
    {
        probe.Reached.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return NoContent();
    }

    private sealed class SyntheticDatabaseException() : DbException("sensitive-test-marker");
}

public sealed class EchoRequest
{
    [Required]
    public string? Name { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; }

    [Required]
    public UsdMoney? Amount { get; init; }

    public decimal Ratio { get; init; }
}
