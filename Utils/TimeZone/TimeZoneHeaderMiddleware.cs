namespace VibeTrade.Backend.Utils.TimeZone;

public sealed class TimeZoneHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Timezone";

    public Task InvokeAsync(HttpContext context, RequestTimeZoneContext tz)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var v))
            tz.TimeZoneId = v.FirstOrDefault();
        return next(context);
    }
}
