namespace OneNoteMdExport.Util;

/// <summary>
/// Token-bucket rate limiter.  Each <see cref="WaitAsync"/> call acquires one slot
/// and releases it after <paramref name="windowSeconds"/> seconds, keeping the
/// request rate at most <paramref name="maxPerWindow"/> per window.
/// </summary>
public sealed class Throttle(Int32 maxPerWindow, Double windowSeconds) : IDisposable
{
    private readonly SemaphoreSlim _bucket = new SemaphoreSlim(maxPerWindow, maxPerWindow);
    private readonly TimeSpan _window = TimeSpan.FromSeconds(windowSeconds);

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _bucket.WaitAsync(ct);
        _ = Task.Delay(_window, ct).ContinueWith(
            _ =>
            {
                try { _bucket.Release(); }
                catch (ObjectDisposedException) { }
            },
            TaskScheduler.Default);
    }

    public void Dispose() => _bucket.Dispose();
}
