using System.Diagnostics;

namespace PeopleCore.Api.Runtime;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try { await next(context); }
        finally
        {
            sw.Stop();
            logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms", context.Request.Method, context.Request.Path.Value ?? "/", context.Response.StatusCode, sw.Elapsed.TotalMilliseconds);
        }
    }
}
