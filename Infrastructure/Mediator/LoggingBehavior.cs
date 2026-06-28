using System.Diagnostics;
using MediatR;

namespace VibeTrade.Backend.Infrastructure.Mediator;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        logger.LogDebug("MediatR handling {RequestName}", name);
        var sw = Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            sw.Stop();
            logger.LogDebug("MediatR handled {RequestName} in {ElapsedMs}ms", name, sw.ElapsedMilliseconds);
        }
    }
}
