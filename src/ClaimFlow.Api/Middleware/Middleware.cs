using ClaimFlow.Application.Common;
using ClaimFlow.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Middleware;

/// <summary>
/// Maps expected application exceptions to RFC 7807 problem details (400/404/409) without error-level logging.
/// Anything unexpected is re-thrown to UseExceptionHandler, which logs it and returns a generic 500.
/// </summary>
public sealed class ProblemDetailsMappingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (Map(ex) is { } problem && !context.Response.HasStarted)
        {
            context.Response.Clear();
            problem.Extensions["traceId"] = context.TraceIdentifier;
            IResult result = problem is ValidationProblemDetails validation
                ? TypedResults.ValidationProblem(validation.Errors, title: validation.Title, extensions: problem.Extensions)
                : TypedResults.Problem(problem);
            await result.ExecuteAsync(context);
        }
    }

    private static ProblemDetails? Map(Exception exception) => exception switch
    {
        RequestValidationException v => new ValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed"
        },
        ValidationException v => new ValidationProblemDetails(v.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed"
        },
        NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = exception.Message },
        ConflictException or DomainException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = exception.Message },
        _ => null
    };
}

/// <summary>Propagates/creates an X-Correlation-Id, adds it to the logging scope and sets security headers.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var id = string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString("N") : incoming[..Math.Min(64, incoming.Length)];

        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[Header] = id;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.CacheControl = "no-store"; // API responses may contain PHI
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context);
    }
}
