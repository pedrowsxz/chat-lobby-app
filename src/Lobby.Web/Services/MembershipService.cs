using Lobby.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// THE authorization rule for conversations: you can read or write a conversation
// only if you have a row in ConversationMembers. Every path uses this: the page,
// the history endpoint, the form POST fallback and the hub (through MessageService).
public class MembershipService(AppDbContext db)
{
    public Task<bool> IsMemberAsync(int conversationId, string userId) =>
        db.ConversationMembers.AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId);

    public Task<List<string>> GetMemberIdsAsync(int conversationId) =>
        db.ConversationMembers.Where(m => m.ConversationId == conversationId).Select(m => m.UserId).ToListAsync();
}
