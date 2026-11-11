namespace OrderFlow.Api.Payments;

public sealed class PaymentCallbackSettings
{
    public bool Enabled { get; init; }
    public string KeyId { get; init; } = "development-v1";
    public string Secret { get; init; } = "";

    public static PaymentCallbackSettings Read(IConfiguration configuration)
    {
        var settings = configuration.GetSection("PaymentCallbacks").Get<PaymentCallbackSettings>() ?? new();
        if (settings.Enabled && (settings.Secret.Length is < 32 or > 512 || settings.KeyId.Length is < 1 or > 80
            || settings.KeyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
            throw new InvalidOperationException("Enabled PaymentCallbacks requires a private 32–512 character Secret and a valid KeyId.");
        return settings;
    }
}
