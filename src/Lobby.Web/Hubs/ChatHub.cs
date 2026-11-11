using Lobby.Web.Data;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Hubs;

// Authentication: the browser sends the same Identity cookie on the negotiate request and the
// WebSocket upgrade, so Context.UserIdentifier is the logged-in user's id (the NameIdentifier claim).
[Authorize]
public class ChatHub(MessageService messages, PresenceTracker presence, AppDbContext db, TimeProvider time) : Hub
{
    // All connections of non-guest users. A stable audience, so a group fits here:
    // it is the set of people allowed to see a registered user's presence.
    public const string DirectoryGroup = "directory";

    private string UserId => Context.UserIdentifier!;

    public async Task<MessageDto> SendMessage(int conversationId, string body, Guid clientMessageId)
    {
        var result = await messages.SendAsync(UserId, conversationId, body, clientMessageId);

        // HubException is the only exception whose message reaches the client. Anything else
        // becomes a generic error (EnableDetailedErrors is off outside Development).
        return result.Error switch
        {
            SendError.None => result.Message!,
            SendError.InvalidBody => throw new HubException("A mensagem precisa ter de 1 a 2000 caracteres."),
            SendError.RateLimited => throw new HubException("Você está enviando rápido demais. Espere alguns segundos."),
            _ => throw new HubException("Conversa não encontrada"), // non-member: same as "doesn't exist"
        };
    }

    public override async Task OnConnectedAsync()
    {
        // Cross-site WebSocket hijacking: SameSite=Lax keeps the cookie off handshakes from other
        // SITES, but sibling subdomains (other-app.example.com) count as the same site. Browsers
        // always send Origin on a WebSocket handshake, so refuse any origin that isn't ours.
        var request = Context.GetHttpContext()!.Request;
        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != $"{request.Scheme}://{request.Host}")
        {
            Context.Abort();
            return;
        }

        var isGuest = Context.User!.IsGuest(); // from the cookie claim: no database query
        var cameOnline = presence.Connect(UserId, Context.ConnectionId);
        if (!isGuest)
            await Groups.AddToGroupAsync(Context.ConnectionId, DirectoryGroup);
        if (cameOnline)
            await AnnouncePresenceAsync(isGuest, online: true, lastSeenAt: null);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Only the LAST connection closing makes the user offline (two tabs = two connections).
        if (presence.Disconnect(UserId, Context.ConnectionId))
        {
            var lastSeenAt = time.GetUtcNow();
            await db.Users.Where(u => u.Id == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, lastSeenAt));
            // Navigating to another page closes one connection and opens a new one. If the new one
            // arrived while we were saving, it has already announced "online": don't undo that.
            if (!presence.IsOnline(UserId))
                await AnnouncePresenceAsync(Context.User!.IsGuest(), online: false, lastSeenAt);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task AnnouncePresenceAsync(bool isGuest, bool online, DateTimeOffset? lastSeenAt)
    {
        if (!isGuest)
        {
            await Clients.Group(DirectoryGroup).SendAsync("presenceChanged", UserId, online, lastSeenAt);
            return;
        }

        // A guest is visible only to their pair, so only the pair hears about it. The pair id is
        // read from the database because it can be set after the guest's cookie was issued.
        var pairId = await db.Users.Where(u => u.Id == UserId).Select(u => u.GuestPairId).FirstOrDefaultAsync();
        if (pairId is not null)
            await Clients.User(pairId).SendAsync("presenceChanged", UserId, online, lastSeenAt);
    }
}
