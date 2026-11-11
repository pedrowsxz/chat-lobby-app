using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.SignalR;

namespace Lobby.Web.Hubs;

// The one abstraction in this app, kept for tests: MessageService depends on this
// interface, so a test can swap in a fake and check exactly what was (or wasn't) broadcast.
public interface IChatBroadcaster
{
    Task MessageCreatedAsync(IReadOnlyList<string> memberIds, MessageDto message);
}

public class ChatBroadcaster(IHubContext<ChatHub> hub) : IChatBroadcaster
{
    // Clients.Users, not a SignalR group per conversation (ADR 0002): SignalR maps each
    // user id to all of that user's open connections. The member list comes from the
    // database on every send, so someone added a second ago gets the message, and someone
    // who left doesn't, with nothing to keep in sync.
    public Task MessageCreatedAsync(IReadOnlyList<string> memberIds, MessageDto message) =>
        hub.Clients.Users(memberIds).SendAsync("messageReceived", message);
}
