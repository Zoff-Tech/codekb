namespace CodeKb.Embedding;

public sealed class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly double _baseSeconds;
    private readonly Func<int, TimeSpan> _delayFunc;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    public RetryPolicy(int maxRetries, double baseSeconds, Func<int, TimeSpan>? delayFunc = null, Func<TimeSpan, CancellationToken, Task>? sleep = null)
    {
        if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
        if (baseSeconds < 0) throw new ArgumentOutOfRangeException(nameof(baseSeconds));
        _maxRetries = maxRetries;
        _baseSeconds = baseSeconds;
        _delayFunc = delayFunc ?? DefaultDelay;
        _sleep = sleep ?? Task.Delay;
    }

    public TimeSpan DefaultDelay(int attempt)
    {
        // attempt is 0-indexed; first retry uses base, then exponential
        var seconds = _baseSeconds * Math.Pow(2, attempt);
        return TimeSpan.FromSeconds(seconds);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception, bool>? shouldRetry, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (attempt < _maxRetries && (shouldRetry?.Invoke(ex) ?? true) && !ct.IsCancellationRequested)
            {
                var delay = _delayFunc(attempt);
                await _sleep(delay, ct);
                attempt++;
            }
        }
    }
}
