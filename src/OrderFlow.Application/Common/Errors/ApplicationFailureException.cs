namespace OrderFlow.Application.Common.Errors;

public sealed class ApplicationFailureException(
    ApplicationError error,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(error.ToString())
{
    public ApplicationError Error { get; } = error;

    // Messages must be developer-authored; never interpolate attempted values/secrets.
    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();
}
