using ProjectTraiding.CustomFeatures.Errors;

namespace ProjectTraiding.CustomFeatures.Clients;

public static class CalendarHttpClientHelpers
{
    public static string ClassifyStatus(int statusCode) => statusCode switch
    {
        429 => CalendarErrorTypes.RateLimit,
        408 => CalendarErrorTypes.Timeout,
        401 or 403 => CalendarErrorTypes.Auth,
        400 => CalendarErrorTypes.BadRequest,
        404 => CalendarErrorTypes.NotFound,
        >= 500 => CalendarErrorTypes.ServerError,
        >= 400 => CalendarErrorTypes.ClientError,
        _ => CalendarErrorTypes.UnexpectedStatus
    };

    public static void EnsureSuccessOrThrow(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode)
            return;

        int status = (int)response.StatusCode;
        response.Dispose();

        if (status == 408)
            throw new CalendarTimeoutException(endpoint);

        throw new CalendarHttpStatusException(endpoint, status, ClassifyStatus(status));
    }

    public static TimeSpan? TryParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfterHeader = response.Headers.RetryAfter;
        if (retryAfterHeader is null)
            return null;

        if (retryAfterHeader.Delta is TimeSpan delta)
            return delta;

        if (retryAfterHeader.Date is DateTimeOffset date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    public static TimeSpan? GetRetryAfterForPolly(HttpResponseMessage response, TimeSpan maxDelay)
    {
        TimeSpan? raw = TryParseRetryAfter(response);
        if (raw is null)
            return null;

        if (raw.Value <= TimeSpan.Zero)
            raw = TimeSpan.FromSeconds(1);

        return raw.Value > maxDelay ? maxDelay : raw.Value;
    }
}
