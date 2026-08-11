using System.Collections.Concurrent;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Helper class to validate incoming LocalSend PIN authentication.
/// ponytail: Split out purely to keep LocalSendServerHelper.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendPinValidator
{
    public static bool CheckPin(
        string? configuredPin,
        ConcurrentDictionary<string, int> pinAttempts,
        string clientIp,
        string? requestPin,
        out int statusCode,
        out string? jsonResponseBody)
    {
        statusCode = 200;
        jsonResponseBody = null;

        if (string.IsNullOrEmpty(configuredPin)) return true;

        var attempts = pinAttempts.TryGetValue(clientIp, out var val) ? val : 0;
        if (attempts >= 3)
        {
            statusCode = 429;
            jsonResponseBody = "{\"message\":\"Too many requests\"}";
            return false;
        }

        if (requestPin != configuredPin)
        {
            if (!string.IsNullOrEmpty(requestPin))
                pinAttempts.AddOrUpdate(clientIp, 1, (_, old) => old + 1);

            statusCode = 401;
            jsonResponseBody = string.IsNullOrEmpty(requestPin)
                ? "{\"message\":\"PIN required\"}"
                : "{\"message\":\"Invalid PIN\"}";
            return false;
        }

        pinAttempts.TryRemove(clientIp, out _);
        return true;
    }
}
