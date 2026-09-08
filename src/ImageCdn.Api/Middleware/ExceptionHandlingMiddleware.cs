using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ImageCdn.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            ImageNotFoundException => (
                StatusCodes.Status404NotFound,
                "Image not found",
                exception.Message),
            ImageConflictException => (
                StatusCodes.Status409Conflict,
                "Image already exists",
                exception.Message),
            UpstreamProviderException upe => (
                upe.StatusCode is >= 500 or null or < 400
                    ? StatusCodes.Status502BadGateway
                    : upe.StatusCode.Value,
                "Upstream provider failure",
                upe.Message),
            ProviderUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Provider unavailable",
                exception.Message),
            InvalidOperationException ioe when ioe.Message.Contains("CLOUDFLARE", StringComparison.OrdinalIgnoreCase)
                || ioe.Message.Contains("IMAGE_PROVIDER", StringComparison.OrdinalIgnoreCase) => (
                StatusCodes.Status503ServiceUnavailable,
                "Provider configuration unavailable",
                ioe.Message),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (
                499,
                "Request cancelled",
                "The client cancelled the request."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred.")
        };

        if (status == 499)
        {
            context.Response.StatusCode = status;
            return;
        }

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.com/{status}"
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        if (exception is UpstreamProviderException upstream && !string.IsNullOrWhiteSpace(upstream.ProviderCode))
        {
            problem.Extensions["providerCode"] = upstream.ProviderCode;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
