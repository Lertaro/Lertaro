using System.Collections.Concurrent;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Helper class to validate incoming LocalSend PIN authentication.
/// ponytail: Split out purely to keep LocalSendServerHelper.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendPinValidator
{
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public static bool CheckPin(
        string? configuredPin,
        ConcurrentDictionary<string, int> pinAttempts,
        string clientIp,
        string? requestPin,
        out int statusCode,
        out string? jsonResponseBody,
        ConcurrentDictionary<string, DateTime>? attemptTimes = null)
    {
        statusCode = 200;
        jsonResponseBody = null;

        if (string.IsNullOrEmpty(configuredPin)) return true;

        var attempts = pinAttempts.TryGetValue(clientIp, out var val) ? val : 0;
        if (attempts >= 3)
        {
            // Let a lockout expire after a few minutes instead of blocking the IP until restart.
            if (attemptTimes != null &&
                attemptTimes.TryGetValue(clientIp, out var lastAttempt) &&
                DateTime.UtcNow - lastAttempt >= LockoutDuration)
            {
                pinAttempts.TryRemove(clientIp, out _);
                attemptTimes.TryRemove(clientIp, out _);
                attempts = 0;
            }
            else
            {
                statusCode = 429;
                jsonResponseBody = "{\"message\":\"Too many requests\"}";
                return false;
            }
        }

        if (requestPin != configuredPin)
        {
            if (!string.IsNullOrEmpty(requestPin))
            {
                pinAttempts.AddOrUpdate(clientIp, 1, (_, old) => old + 1);
                attemptTimes?.AddOrUpdate(clientIp, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            }

            statusCode = 401;
            jsonResponseBody = string.IsNullOrEmpty(requestPin)
                ? "{\"message\":\"PIN required\"}"
                : "{\"message\":\"Invalid PIN\"}";
            return false;
        }

        pinAttempts.TryRemove(clientIp, out _);
        attemptTimes?.TryRemove(clientIp, out _);
        return true;
    }
}
