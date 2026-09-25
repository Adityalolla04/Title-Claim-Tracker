// File: TitleClaimTracker/Middleware/GlobalExceptionMiddleware.cs
using Microsoft.AspNetCore.Mvc;
using TitleClaimTracker.Core.Exceptions;

namespace TitleClaimTracker.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure for {Path}", context.Request.Path);
            context.Response.StatusCode = exception switch
            {
                UnauthorizedAccessException when context.User.Identity?.IsAuthenticated == true => StatusCodes.Status403Forbidden,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                KeyNotFoundException => StatusCodes.Status404NotFound,
                ArgumentException => StatusCodes.Status400BadRequest,
                InvalidFilingStatusTransitionException => StatusCodes.Status409Conflict,
                ConcurrencyConflictException => StatusCodes.Status409Conflict,
                IdempotencyConflictException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails { Status = context.Response.StatusCode, Title = "Request failed", Detail = context.Response.StatusCode == 500 ? "An unexpected error occurred." : exception.Message, Instance = context.Request.Path };
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
