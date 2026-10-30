using OrderFlow.Application.Common.Errors;

namespace OrderFlow.Application.Common.Pagination;

public sealed record PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public int Page { get; }
    public int PageSize { get; }
    public int Offset => (Page - 1) * PageSize;

    private PageRequest(int page, int pageSize) => (Page, PageSize) = (page, pageSize);

    public static PageRequest Create(int page = DefaultPage, int pageSize = DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        if (errors.Count == 0 && (long)(page - 1) * pageSize > int.MaxValue)
        {
            errors["page"] = ["The requested page exceeds the supported offset range."];
        }

        if (errors.Count > 0)
        {
            throw new ApplicationFailureException(ApplicationError.ValidationFailed, errors);
        }

        return new PageRequest(page, pageSize);
    }
}
