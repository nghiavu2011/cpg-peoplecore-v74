using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PeopleCore.Api.Runtime;

/// <summary>
/// Converts expected domain/authorization failures raised inside services into
/// stable API responses without exposing stack traces or database details.
/// Unexpected exceptions continue to the platform exception handler as 500.
/// </summary>
public sealed class DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden, ex.Message, ex);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteAsync(context, StatusCodes.Status404NotFound, ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            var status = StatusFor(ex.Message);
            await WriteAsync(context, status, ex.Message, ex);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Database update rejected for correlation {CorrelationId}", Correlation(context));
            await WritePayloadAsync(context, StatusCodes.Status409Conflict, "DATABASE_CONSTRAINT_REJECTED");
        }
    }

    private async Task WriteAsync(HttpContext context, int status, string code, Exception ex)
    {
        logger.LogWarning(ex, "PeopleCore request rejected with {Code} ({Status}) correlation {CorrelationId}", code, status, Correlation(context));
        await WritePayloadAsync(context, status, SafeCode(code));
    }

    private static async Task WritePayloadAsync(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var payload = new
        {
            type = "about:blank",
            title = status switch
            {
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Bad Request"
            },
            status,
            code,
            correlationId = Correlation(context)
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static int StatusFor(string? code)
    {
        code ??= string.Empty;
        if (code.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return StatusCodes.Status404NotFound;
        if (code.Contains("ALREADY", StringComparison.OrdinalIgnoreCase)
            || code.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
            || code.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
            || code.Contains("OVERLAP", StringComparison.OrdinalIgnoreCase)
            || code.Contains("NOT_PENDING", StringComparison.OrdinalIgnoreCase)
            || code.Contains("VERSION", StringComparison.OrdinalIgnoreCase)) return StatusCodes.Status409Conflict;
        return StatusCodes.Status400BadRequest;
    }

    private static string SafeCode(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "REQUEST_REJECTED" : value.Trim().ToUpperInvariant();
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').Take(120).ToArray();
        return new string(chars);
    }

    private static string? Correlation(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var id) ? id?.ToString() : null;
}
