using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace VibeTrade.Backend.Utils;

/// <summary>Configuration for retrying HTTP requests when the server returns 429 Too Many Requests.</summary>
public sealed record TooManyRequestsRetryOptions
{
    /// <summary>Maximum wall-clock time from the first attempt until retries stop.</summary>
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Optional label for log messages (for example the outbound integration name).</summary>
    public string? OperationName { get; init; }
}

/// <summary>Shared helpers for outbound HTTP and similar call patterns.</summary>
public static class GeneralUtils
{
    /// <summary>
    /// Invokes <paramref name="sendAsync"/> in a loop: on HTTP 429, waits (honoring Retry-After when present,
    /// otherwise exponential backoff) until <see cref="TooManyRequestsRetryOptions.TotalBudget"/> elapses, then returns the last 429 response.
    /// </summary>
    /// <remarks>The caller owns the returned <see cref="HttpResponseMessage"/> and must dispose it.</remarks>
    public static async Task<HttpResponseMessage> SendWithTooManyRequestsRetryAsync(
        Func<Task<HttpResponseMessage>> sendAsync,
        TooManyRequestsRetryOptions options,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentNullException.ThrowIfNull(options);

        var deadline = DateTimeOffset.UtcNow.Add(options.TotalBudget);
        var backoff = options.InitialBackoff;
        var opLabel = string.IsNullOrWhiteSpace(options.OperationName) ? "HTTP" : options.OperationName.Trim();

        while (true)
        {
            var response = await sendAsync().ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger?.LogDebug(
                    "{Operation}: HTTP 429 after ~{Budget}s retry budget; returning response to caller.",
                    opLabel,
                    options.TotalBudget.TotalSeconds);
                return response;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            var wait = Compute429Wait(response, backoff, options.MaxBackoff, remaining);

            if (wait <= TimeSpan.Zero)
            {
                logger?.LogDebug(
                    "{Operation}: HTTP 429 with no remaining time in the {Budget}s budget; returning response to caller.",
                    opLabel,
                    options.TotalBudget.TotalSeconds);
                return response;
            }

            logger?.LogInformation(
                "{Operation}: HTTP 429, waiting {WaitMs}ms before retry (~{RemainingMs}ms left in budget).",
                opLabel,
                (int)wait.TotalMilliseconds,
                (int)remaining.TotalMilliseconds);

            response.Dispose();
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

            var nextMs = Math.Min(backoff.TotalMilliseconds * 2.0, options.MaxBackoff.TotalMilliseconds);
            backoff = TimeSpan.FromMilliseconds(nextMs);
        }
    }

    private static TimeSpan Compute429Wait(
        HttpResponseMessage response,
        TimeSpan currentBackoff,
        TimeSpan maxBackoff,
        TimeSpan remainingBudget)
    {
        var wait = currentBackoff;

        if (response.Headers.TryGetValues(HeaderNames.RetryAfter, out var raValues))
        {
            var raw = raValues.FirstOrDefault()?.Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sec) && sec > 0)
            {
                var fromHeader = TimeSpan.FromSeconds(Math.Min(sec, (int)maxBackoff.TotalSeconds));
                wait = wait > fromHeader ? wait : fromHeader;
            }
        }
        else if (response.Headers.RetryAfter?.Delta is { TotalMilliseconds: > 0 } delta)
        {
            var capped = delta > maxBackoff ? maxBackoff : delta;
            wait = wait > capped ? wait : capped;
        }

        if (wait > remainingBudget)
            wait = remainingBudget;

        return wait;
    }
}
