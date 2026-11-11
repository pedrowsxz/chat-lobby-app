using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// Read-only queries for the chat pages. Callers check membership first.
// Each method is one projected, AsNoTracking query: no N+1.
public class ConversationQueries(AppDbContext db)
{
    public const int PageSize = 30;
    public const int CatchUpLimit = 200;

    // The sidebar in one SQL statement: preview, unread count and the other person
    // are correlated subqueries, not one query per conversation.
    public Task<List<ConversationListItem>> GetSidebarAsync(string userId) =>
        db.ConversationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new ConversationListItem
            {
                Id = m.ConversationId,
                Type = m.Conversation.Type,
                GroupName = m.Conversation.Name,
                LastActivityAt = m.Conversation.LastMessageAt ?? m.Conversation.CreatedAt,
                LastMessagePreview = m.Conversation.Messages
                    .OrderByDescending(x => x.Id).Select(x => x.Body.Substring(0, 80)).FirstOrDefault(),
                UnreadCount = m.Conversation.Messages
                    .Count(x => x.SenderId != userId && (m.LastReadAt == null || x.SentAt > m.LastReadAt)),
                Other = m.Conversation.Type == ConversationType.Direct
                    ? m.Conversation.Members.Where(o => o.UserId != userId)
                        .Select(o => new PersonSummary(o.User.Id, o.User.DisplayName, o.User.AvatarFileName, o.User.IsDemoBot, o.User.LastSeenAt))
                        .FirstOrDefault()
                    : null,
            })
            .OrderByDescending(x => x.LastActivityAt)
            .ToListAsync();

    public Task<ConversationHeader?> GetHeaderAsync(int conversationId, string userId) =>
        db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new ConversationHeader
            {
                Id = c.Id,
                Type = c.Type,
                Name = c.Name,
                MemberCount = c.Members.Count,
                Other = c.Type == ConversationType.Direct
                    ? c.Members.Where(o => o.UserId != userId)
                        .Select(o => new PersonSummary(o.User.Id, o.User.DisplayName, o.User.AvatarFileName, o.User.IsDemoBot, o.User.LastSeenAt))
                        .FirstOrDefault()
                    : null,
            })
            .FirstOrDefaultAsync();

    // Keyset paging on Id. "before": the 30 messages older than that id. "after": everything
    // newer (reconnect catch-up, capped). Always returned oldest first, ready to render.
    public async Task<MessagePage> GetMessagesAsync(int conversationId, int? before = null, int? after = null)
    {
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);

        if (after is not null)
        {
            var newer = await query.Where(m => m.Id > after).OrderBy(m => m.Id)
                .Take(CatchUpLimit + 1).Select(MessageDto.FromMessage).ToListAsync();
            var tooMany = newer.Count > CatchUpLimit;
            if (tooMany) newer.RemoveAt(CatchUpLimit);
            return new MessagePage(newer, tooMany);
        }

        if (before is not null) query = query.Where(m => m.Id < before);

        // Fetch one extra row only to know whether there is an older page.
        var page = await query.OrderByDescending(m => m.Id)
            .Take(PageSize + 1).Select(MessageDto.FromMessage).ToListAsync();
        var hasMore = page.Count > PageSize;
        if (hasMore) page.RemoveAt(PageSize);
        page.Reverse();
        return new MessagePage(page, hasMore);
    }
}
