using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VisionRP.SupportServer.Models;

namespace VisionRP.SupportServer.Services;

public sealed class TicketService : ITicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _dataPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public TicketService(IOptions<SupportOptions> options, IHostEnvironment environment)
    {
        _dataPath = string.IsNullOrWhiteSpace(options.Value.DataPath)
            ? Path.Combine(environment.ContentRootPath, "data", "tickets.json")
            : Path.GetFullPath(options.Value.DataPath);
    }

    public async Task<(TicketCreatedResponse Created, TicketResponse Ticket)> CreateAsync(
        CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        var accessCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var ticket = new StoredTicket(
            CreateTicketId(),
            request.PlayerName.Trim(),
            request.Contact.Trim(),
            request.Category.Trim(),
            request.Subject.Trim(),
            TicketStatus.Open,
            now,
            now,
            HashAccessCode(accessCode),
            [new StoredMessage("Spieler", request.Message.Trim(), now)]);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var tickets = await ReadTicketsAsync(cancellationToken);
            while (tickets.Any(item => item.Id == ticket.Id))
            {
                ticket = ticket with { Id = CreateTicketId() };
            }

            tickets.Add(ticket);
            await WriteTicketsAsync(tickets, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }

        return (new TicketCreatedResponse(ticket.Id, accessCode, now), ToResponse(ticket));
    }

    public async Task<TicketResponse?> GetForPlayerAsync(
        string ticketId,
        string accessCode,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var ticket = (await ReadTicketsAsync(cancellationToken))
                .FirstOrDefault(item => item.Id.Equals(ticketId, StringComparison.OrdinalIgnoreCase));
            return ticket is not null && MatchesAccessCode(ticket, accessCode) ? ToResponse(ticket) : null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<TicketResponse?> AddPlayerMessageAsync(
        string ticketId,
        string accessCode,
        string message,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var tickets = await ReadTicketsAsync(cancellationToken);
            var index = tickets.FindIndex(item => item.Id.Equals(ticketId, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || !MatchesAccessCode(tickets[index], accessCode))
            {
                return null;
            }

            var ticket = tickets[index];
            if (ticket.Status == TicketStatus.Closed)
            {
                return null;
            }

            ticket = AddMessage(ticket, "Spieler", message);
            tickets[index] = ticket;
            await WriteTicketsAsync(tickets, cancellationToken);
            return ToResponse(ticket);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<TicketResponse>> ListForSupportAsync(
        TicketStatus? status,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var tickets = await ReadTicketsAsync(cancellationToken);
            return tickets
                .Where(ticket => status is null || ticket.Status == status)
                .OrderByDescending(ticket => ticket.UpdatedAt)
                .Select(ToResponse)
                .ToArray();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<TicketResponse?> GetForSupportAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var ticket = (await ReadTicketsAsync(cancellationToken))
                .FirstOrDefault(item => item.Id.Equals(ticketId, StringComparison.OrdinalIgnoreCase));
            return ticket is null ? null : ToResponse(ticket);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<TicketResponse?> AddSupportMessageAsync(
        string ticketId,
        string message,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var tickets = await ReadTicketsAsync(cancellationToken);
            var index = tickets.FindIndex(item => item.Id.Equals(ticketId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var ticket = AddMessage(tickets[index], "Support", message);
            if (ticket.Status == TicketStatus.Open)
            {
                ticket = ticket with { Status = TicketStatus.WaitingForPlayer };
            }

            tickets[index] = ticket;
            await WriteTicketsAsync(tickets, cancellationToken);
            return ToResponse(ticket);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<TicketResponse?> UpdateStatusAsync(
        string ticketId,
        TicketStatus status,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var tickets = await ReadTicketsAsync(cancellationToken);
            var index = tickets.FindIndex(item => item.Id.Equals(ticketId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var ticket = tickets[index] with { Status = status, UpdatedAt = DateTimeOffset.UtcNow };
            tickets[index] = ticket;
            await WriteTicketsAsync(tickets, cancellationToken);
            return ToResponse(ticket);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<List<StoredTicket>> ReadTicketsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_dataPath);
        return await JsonSerializer.DeserializeAsync<List<StoredTicket>>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Ticket data at '{_dataPath}' is empty or invalid.");
    }

    private async Task WriteTicketsAsync(List<StoredTicket> tickets, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_dataPath)
            ?? throw new InvalidOperationException("The ticket data path must include a directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_dataPath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, tickets, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _dataPath, overwrite: true);
    }

    private static StoredTicket AddMessage(StoredTicket ticket, string author, string message)
    {
        var now = DateTimeOffset.UtcNow;
        var status = author == "Spieler"
            && (ticket.Status is TicketStatus.WaitingForPlayer or TicketStatus.Resolved)
            ? TicketStatus.InProgress
            : ticket.Status;
        return ticket with
        {
            Status = status,
            UpdatedAt = now,
            Messages = [.. ticket.Messages, new StoredMessage(author, message.Trim(), now)]
        };
    }

    private static bool MatchesAccessCode(StoredTicket ticket, string accessCode)
    {
        var expectedHash = Convert.FromHexString(ticket.AccessCodeHash);
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(accessCode));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static string HashAccessCode(string accessCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessCode)));

    private static string CreateTicketId() => $"VRP-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";

    private static TicketResponse ToResponse(StoredTicket ticket) =>
        new(
            ticket.Id,
            ticket.PlayerName,
            ticket.Contact,
            ticket.Category,
            ticket.Subject,
            ticket.Status,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.Messages.Select(message =>
                new TicketMessageResponse(message.Author, message.Content, message.CreatedAt)).ToArray());

    private sealed record StoredTicket(
        string Id,
        string PlayerName,
        string Contact,
        string Category,
        string Subject,
        TicketStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string AccessCodeHash,
        List<StoredMessage> Messages);

    private sealed record StoredMessage(string Author, string Content, DateTimeOffset CreatedAt);
}
