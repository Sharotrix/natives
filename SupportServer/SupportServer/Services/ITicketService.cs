using VisionRP.SupportServer.Models;

namespace VisionRP.SupportServer.Services;

public interface ITicketService
{
    Task<(TicketCreatedResponse Created, TicketResponse Ticket)> CreateAsync(
        CreateTicketRequest request,
        CancellationToken cancellationToken);

    Task<TicketResponse?> GetForPlayerAsync(
        string ticketId,
        string accessCode,
        CancellationToken cancellationToken);

    Task<TicketResponse?> AddPlayerMessageAsync(
        string ticketId,
        string accessCode,
        string message,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketResponse>> ListForSupportAsync(
        TicketStatus? status,
        CancellationToken cancellationToken);

    Task<TicketResponse?> GetForSupportAsync(
        string ticketId,
        CancellationToken cancellationToken);

    Task<TicketResponse?> AddSupportMessageAsync(
        string ticketId,
        string message,
        CancellationToken cancellationToken);

    Task<TicketResponse?> UpdateStatusAsync(
        string ticketId,
        TicketStatus status,
        CancellationToken cancellationToken);
}
