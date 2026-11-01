using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VisionRP.SupportServer.Middleware;

public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        logger.LogWarning(exception, "Rejected malformed support API request.");
        httpContext.Response.StatusCode = badRequest.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = badRequest.StatusCode,
            Title = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "Payload Too Large"
                : "Bad Request",
            Detail = "The request body or parameters are invalid.",
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
