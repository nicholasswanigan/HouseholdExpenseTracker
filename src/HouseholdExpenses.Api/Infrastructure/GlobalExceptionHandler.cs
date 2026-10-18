using HouseholdExpenses.Core.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdExpenses.Api.Infrastructure;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 problem responses and logs them.
/// Business-rule violations become 400s; anything else is a 500 that never leaks internals.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case DomainValidationException:
                logger.LogInformation("Validation failed: {Message}", exception.Message);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed",
                    Detail = exception.Message
                };
                break;

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return true; // client went away; nothing to send

            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred",
                    Detail = "Something went wrong on our side. Please try again."
                };
                break;
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
