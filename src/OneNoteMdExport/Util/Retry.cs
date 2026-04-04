using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

namespace OneNoteMdExport.Util;

public static class Retry
{
    private static readonly int[] RetryableStatus = [429, 500, 502, 503, 504];
    private static readonly object SyncRoot = new();
    private static Throttle _minuteThrottle = new(100, 60.0);
    private static Throttle _hourThrottle = new(350, 3600.0);
    private static SemaphoreSlim _concurrentRequests = new(5, 5);

    public static void Configure(int requestsPerMinute, int requestsPerHour, int concurrentRequests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerHour);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentRequests);

        lock (SyncRoot)
        {
            var minuteThrottle = new Throttle(requestsPerMinute, 60.0);
            var hourThrottle = new Throttle(requestsPerHour, 3600.0);
            var concurrentGate = new SemaphoreSlim(concurrentRequests, concurrentRequests);

            var oldMinute = _minuteThrottle;
            var oldHour = _hourThrottle;
            var oldConcurrent = _concurrentRequests;

            _minuteThrottle = minuteThrottle;
            _hourThrottle = hourThrottle;
            _concurrentRequests = concurrentGate;

            oldMinute.Dispose();
            oldHour.Dispose();
            oldConcurrent.Dispose();
        }
    }

    /// <summary>
    /// Executes <paramref name="action"/> up to <paramref name="maxRetries"/> times,
    /// backing off on Graph throttle (429) and transient server errors.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        int maxRetries = 5,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await ExecuteThrottledAsync(action, ct);
            }
            catch (ApiException ex) when (RetryableStatus.Contains(ex.ResponseStatusCode))
            {
                var delay = RetryDelay(ex, attempt);
                logger?.LogWarning(
                    "HTTP {Status} (attempt {A}/{Max}) — retrying in {S}s",
                    ex.ResponseStatusCode, attempt + 1, maxRetries, (int)delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                logger?.LogWarning(
                    "Network error (attempt {A}/{Max}) — retrying in {S}s: {Msg}",
                    attempt + 1, maxRetries, (int)delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt — let any exception propagate
        return await ExecuteThrottledAsync(action, ct);
    }

    private static async Task<T> ExecuteThrottledAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var minuteThrottle = _minuteThrottle;
        var hourThrottle = _hourThrottle;
        var concurrentRequests = _concurrentRequests;

        await minuteThrottle.WaitAsync(ct);
        await hourThrottle.WaitAsync(ct);
        await concurrentRequests.WaitAsync(ct);

        try
        {
            return await action();
        }
        finally
        {
            concurrentRequests.Release();
        }
    }

    private static TimeSpan RetryDelay(ApiException ex, int attempt)
    {
        // Honour Retry-After header when present
        if (ex.ResponseHeaders is not null &&
            ex.ResponseHeaders.TryGetValue("Retry-After", out var values))
        {
            var header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out var secs))
                return TimeSpan.FromSeconds(secs);

            if (header is not null && DateTimeOffset.TryParse(header, out var retryAt))
            {
                var delay = retryAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    return delay;
            }
        }

        // Exponential back-off: 2 s, 4 s, 8 s, …
        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    }
}
