using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VisionRP.SupportServer.Services;

namespace VisionRP.SupportServer.Middleware;

public sealed class AdminAuthorizationFilter(IOptions<SupportOptions> options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
        var suppliedToken = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : string.Empty;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.AdminToken));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));

        if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
        {
            return ValueTask.FromResult<object?>(TypedResults.Unauthorized());
        }

        return next(context);
    }
}
