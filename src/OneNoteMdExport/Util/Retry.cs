using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;

namespace OneNoteMdExport.Util;

public static class Retry
{
    private static readonly int[] RetryableStatus = [429, 500, 502, 503, 504];

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
                return await action();
            }
            catch (ODataError ex) when (RetryableStatus.Contains(ex.ResponseStatusCode))
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
        return await action();
    }

    private static TimeSpan RetryDelay(ODataError ex, int attempt)
    {
        // Honour Retry-After header when present
        if (ex.ResponseHeaders is not null &&
            ex.ResponseHeaders.TryGetValue("Retry-After", out var values))
        {
            var header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out var secs))
                return TimeSpan.FromSeconds(secs);
        }

        // Exponential back-off: 2 s, 4 s, 8 s, …
        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    }
}
