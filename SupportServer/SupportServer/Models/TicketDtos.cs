using System.ComponentModel.DataAnnotations;

namespace VisionRP.SupportServer.Models;

/// <summary>Payload for creating a player support ticket.</summary>
public sealed record CreateTicketRequest
{
    /// <summary>Player's display name.</summary>
    [Required, StringLength(80, MinimumLength = 2)]
    public required string PlayerName { get; init; }

    /// <summary>How support can identify or contact the player.</summary>
    [Required, StringLength(120, MinimumLength = 2)]
    public required string Contact { get; init; }

    /// <summary>Ticket category.</summary>
    [Required, StringLength(40, MinimumLength = 2)]
    public required string Category { get; init; }

    /// <summary>Short summary of the issue.</summary>
    [Required, StringLength(120, MinimumLength = 4)]
    public required string Subject { get; init; }

    /// <summary>Detailed description of the issue.</summary>
    [Required, StringLength(5000, MinimumLength = 10)]
    public required string Message { get; init; }
}

/// <summary>One message in a support conversation.</summary>
public sealed record TicketMessageResponse(
    string Author,
    string Content,
    DateTimeOffset CreatedAt);

/// <summary>Ticket details visible to its player or an authenticated support agent.</summary>
public sealed record TicketResponse(
    string Id,
    string PlayerName,
    string Contact,
    string Category,
    string Subject,
    TicketStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TicketMessageResponse> Messages);

/// <summary>Ticket ID and one-time access code returned after ticket creation.</summary>
public sealed record TicketCreatedResponse(
    string TicketId,
    string AccessCode,
    DateTimeOffset CreatedAt);

/// <summary>Payload for adding a message to an existing ticket.</summary>
public sealed record AddTicketMessageRequest
{
    /// <summary>Message text.</summary>
    [Required, StringLength(5000, MinimumLength = 2)]
    public required string Message { get; init; }
}

/// <summary>Payload for changing a ticket's status.</summary>
public sealed record UpdateTicketStatusRequest
{
    /// <summary>New ticket status.</summary>
    [Required, EnumDataType(typeof(TicketStatus))]
    public required TicketStatus Status { get; init; }
}

/// <summary>Lifecycle state of a support ticket.</summary>
public enum TicketStatus
{
    Open,
    InProgress,
    WaitingForPlayer,
    Resolved,
    Closed
}
