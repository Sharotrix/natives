using Microsoft.AspNetCore.Http.HttpResults;
using VisionRP.SupportServer.Middleware;
using VisionRP.SupportServer.Models;
using VisionRP.SupportServer.Services;

namespace VisionRP.SupportServer.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("GetSupportHealth")
            .WithSummary("Check support service availability");

        var playerTickets = app.MapGroup("/api/tickets")
            .WithTags("Player tickets");

        playerTickets.MapPost("/", async Task<Created<TicketCreatedResponse>> (
            CreateTicketRequest request,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var (created, _) = await service.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"/api/tickets/{created.TicketId}", created);
        })
            .WithName("CreateTicket")
            .WithSummary("Create a support ticket")
            .WithDescription("Creates a ticket and returns its one-time access code.")
            .ProducesValidationProblem()
            .RequireRateLimiting("public-ticket-write");

        playerTickets.MapGet("/{ticketId}", async Task<Results<Ok<TicketResponse>, UnauthorizedHttpResult, NotFound>> (
            string ticketId,
            HttpRequest request,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var accessCode = request.Headers["X-Ticket-Token"].ToString();
            if (string.IsNullOrWhiteSpace(accessCode))
            {
                return TypedResults.Unauthorized();
            }

            var ticket = await service.GetForPlayerAsync(ticketId, accessCode, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("GetPlayerTicket")
            .WithSummary("Read a ticket conversation")
            .WithDescription("Requires the private access code returned when the ticket was created.")
            .Produces<TicketResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        playerTickets.MapPost("/{ticketId}/messages", async Task<Results<Ok<TicketResponse>, UnauthorizedHttpResult, NotFound>> (
            string ticketId,
            AddTicketMessageRequest request,
            HttpRequest httpRequest,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var accessCode = httpRequest.Headers["X-Ticket-Token"].ToString();
            if (string.IsNullOrWhiteSpace(accessCode))
            {
                return TypedResults.Unauthorized();
            }

            var ticket = await service.AddPlayerMessageAsync(ticketId, accessCode, request.Message, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("AddPlayerTicketMessage")
            .WithSummary("Reply to a support ticket as its player")
            .Produces<TicketResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .RequireRateLimiting("public-ticket-write");

        var adminTickets = app.MapGroup("/api/admin/tickets")
            .AddEndpointFilter<AdminAuthorizationFilter>()
            .WithTags("Support administration");

        adminTickets.MapGet("/", async (
            TicketStatus? status,
            ITicketService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListForSupportAsync(status, cancellationToken)))
            .WithName("ListSupportTickets")
            .WithSummary("List support tickets")
            .Produces<IReadOnlyList<TicketResponse>>();

        adminTickets.MapGet("/{ticketId}", async Task<Results<Ok<TicketResponse>, NotFound>> (
            string ticketId,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var ticket = await service.GetForSupportAsync(ticketId, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("GetSupportTicket")
            .WithSummary("Get full support ticket details")
            .Produces<TicketResponse>()
            .Produces(StatusCodes.Status404NotFound);

        adminTickets.MapPost("/{ticketId}/messages", async Task<Results<Ok<TicketResponse>, NotFound>> (
            string ticketId,
            AddTicketMessageRequest request,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var ticket = await service.AddSupportMessageAsync(ticketId, request.Message, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("AddSupportTicketMessage")
            .WithSummary("Reply to a ticket as support")
            .Produces<TicketResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        adminTickets.MapPatch("/{ticketId}/status", async Task<Results<Ok<TicketResponse>, NotFound>> (
            string ticketId,
            UpdateTicketStatusRequest request,
            ITicketService service,
            CancellationToken cancellationToken) =>
        {
            var ticket = await service.UpdateStatusAsync(ticketId, request.Status, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("UpdateSupportTicketStatus")
            .WithSummary("Change a support ticket's status")
            .Produces<TicketResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
    }
}
