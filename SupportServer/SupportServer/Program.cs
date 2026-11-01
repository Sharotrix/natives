using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using VisionRP.SupportServer.Endpoints;
using VisionRP.SupportServer.Middleware;
using VisionRP.SupportServer.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["PORT"] is { } portValue)
{
    if (!int.TryParse(portValue, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException("The PORT environment variable must be a valid TCP port.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<SupportOptions>(builder.Configuration.GetSection("Support"));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("Support:TrustedProxies").Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(proxy, out var address))
        {
            throw new InvalidOperationException($"Support:TrustedProxies contains an invalid IP address: '{proxy}'.");
        }

        options.KnownProxies.Add(address);
    }
});
builder.Services.AddSingleton<ITicketService, TicketService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-ticket-write", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
var supportOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupportOptions>>().Value;
if (string.IsNullOrWhiteSpace(supportOptions.AdminToken)
    || Encoding.UTF8.GetByteCount(supportOptions.AdminToken) < 32)
{
    throw new InvalidOperationException(
        "Set Support__AdminToken to a secret of at least 32 bytes before starting the support server.");
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().ExcludeFromDescription();
}

app.MapTicketEndpoints();

app.Run();
