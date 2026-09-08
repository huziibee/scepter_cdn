using ImageCdn.Api.Options;
using Microsoft.Extensions.Options;

namespace ImageCdn.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<SecurityOptions> securityOptions)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/local-images", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var configuredKey = securityOptions.Value.ServiceApiKey;
        // Empty key: allow unauthenticated access (typical for local Development).
        // Hosting teams must set SERVICE_API_KEY (or replace with their auth) before public exposure.
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            !string.Equals(provided.ToString(), configuredKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7807",
                title = "Unauthorized",
                status = 401,
                detail = "A valid X-Api-Key header is required."
            });
            return;
        }

        await _next(context);
    }
}
