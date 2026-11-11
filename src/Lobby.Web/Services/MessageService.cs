using System.Threading.RateLimiting;
using Lobby.Web.Data;
using Lobby.Web.Hubs;
using Lobby.Web.Models;
using Lobby.Web.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

public enum SendError { None, InvalidBody, NotFound, RateLimited }

public record SendResult(SendError Error, MessageDto? Message = null)
{
    public static SendResult Fail(SendError error) => new(error);
}

// The ONE way a message gets saved. The hub and the no-JS form both call SendAsync.
public class MessageService(
    AppDbContext db,
    MembershipService membership,
    IChatBroadcaster broadcaster,
    PartitionedRateLimiter<string> rateLimiter,
    DemoReplyQueue demoReplies,
    TimeProvider time,
    ILogger<MessageService> logger)
{
    public async Task<SendResult> SendAsync(string senderId, int conversationId, string? body, Guid clientMessageId)
    {
        // 1. Validate: 1–2000 characters after trimming. Then membership (non-members get
        //    "not found", so they can't even learn that the conversation exists). Then the
        //    per-user rate limit. It lives here, not in the ASP.NET rate-limiting middleware,
        //    because that middleware sees HTTP requests, and hub calls all travel on one WebSocket.
        body = body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length > Message.MaxBodyLength || clientMessageId == Guid.Empty)
            return SendResult.Fail(SendError.InvalidBody);
        if (!await membership.IsMemberAsync(conversationId, senderId))
            return SendResult.Fail(SendError.NotFound);
        using (var lease = rateLimiter.AttemptAcquire(senderId))
        {
            if (!lease.IsAcquired) return SendResult.Fail(SendError.RateLimited);
        }

        // 2. Idempotency: a client that lost its connection can't know whether the first
        //    try was saved, so it resends with the same clientMessageId. Return what we have.
        var existing = await FindAsync(senderId, clientMessageId);
        if (existing is not null)
            return existing.ConversationId == conversationId ? new SendResult(SendError.None, existing) : SendResult.Fail(SendError.InvalidBody);

        // 3. Persist first: message + conversation's LastMessageAt + my LastReadAt in one SaveChanges.
        var now = time.GetUtcNow();
        var member = await db.ConversationMembers.Include(m => m.Conversation)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == senderId);
        if (member is null) return SendResult.Fail(SendError.NotFound); // left a moment ago
        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = senderId,
            Body = body,
            SentAt = now,
            ClientMessageId = clientMessageId,
        };
        db.Messages.Add(message);
        member.Conversation.LastMessageAt = now;
        member.LastReadAt = now; // my own message is never unread for me
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // Two copies of the same send arrived at the same time; the unique index kept one,
            // and that one is broadcast by the request that saved it.
            return new SendResult(SendError.None, await FindAsync(senderId, clientMessageId));
        }
        // If SaveChanges threw anything else, we never get here: nothing is broadcast for a
        // message that isn't in the database.

        // 4. Only after the commit: push to every CURRENT member (read now, after the save).
        var dto = await db.Messages.AsNoTracking().Where(m => m.Id == message.Id).Select(MessageDto.FromMessage).SingleAsync();
        var memberIds = await membership.GetMemberIdsAsync(conversationId);
        try
        {
            await broadcaster.MessageCreatedAsync(memberIds, dto);
        }
        catch (Exception ex)
        {
            // The message is saved; the database is the source of truth. Clients that missed the
            // push will get it from history or the reconnect catch-up, so log and carry on.
            logger.LogWarning(ex, "Broadcast failed for message {MessageId}", dto.Id);
        }

        demoReplies.Enqueue(dto); // demo only: lets the bot answer (no-op when disabled)

        // 5. The hub returns this to the sender's browser, which confirms its "enviando…" bubble.
        return new SendResult(SendError.None, dto);
    }

    private Task<MessageDto?> FindAsync(string senderId, Guid clientMessageId) =>
        db.Messages.AsNoTracking()
            .Where(m => m.SenderId == senderId && m.ClientMessageId == clientMessageId)
            .Select(MessageDto.FromMessage)
            .FirstOrDefaultAsync();
}
