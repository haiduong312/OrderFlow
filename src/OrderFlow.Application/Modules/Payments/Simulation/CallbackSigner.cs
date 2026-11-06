using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OrderFlow.Application.Modules.Payments.Simulation;

public sealed record SignedSimulatorCallback(string KeyId, long Timestamp, string Signature, string Body);

public static class CallbackSigner
{
    public static SignedSimulatorCallback Sign(string keyId, string secret, string body, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds();
        var bytes = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body);
        return new(keyId, timestamp, Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)), body);
    }
}
