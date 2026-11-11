using Lobby.Web.Data;
using Lobby.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

public class DirectConversationService(AppDbContext db, PeopleService people, TimeProvider time)
{
    // Returns the id of the one DM between the two users, creating it if needed.
    // Returns null when the target is yourself or someone you are not allowed to see.
    public async Task<int?> GetOrCreateAsync(string myId, string otherUserId)
    {
        // 1. No chats with yourself, and only with people you can see (guest sandbox).
        if (myId == otherUserId) return null;
        if (!await people.CanSeeAsync(myId, otherUserId)) return null;

        // 2. Same pair, same key, whatever the argument order.
        var key = Conversation.MakeDirectKey(myId, otherUserId);
        var existingId = await FindByKeyAsync(key);
        if (existingId is not null) return existingId;

        // 3. Conversation and both members in one SaveChanges, so one INSERT batch in one transaction.
        var now = time.GetUtcNow();
        var conversation = new Conversation
        {
            Type = ConversationType.Direct,
            DirectKey = key,
            CreatedById = myId,
            CreatedAt = now,
            Members =
            [
                new ConversationMember { UserId = myId, JoinedAt = now },
                new ConversationMember { UserId = otherUserId, JoinedAt = now },
            ],
        };
        db.Conversations.Add(conversation);
        try
        {
            await db.SaveChangesAsync();
            return conversation.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // 4. Race: the other person opened the same DM at the same moment. Both of us
            // passed step 2, but the unique index on DirectKey let only one INSERT win.
            // Forget our failed entities and return the conversation that won.
            db.Entry(conversation).State = EntityState.Detached;
            foreach (var member in conversation.Members) db.Entry(member).State = EntityState.Detached;
            return await FindByKeyAsync(key) ?? throw new InvalidOperationException("DM vanished after a duplicate-key error.");
        }
    }

    private Task<int?> FindByKeyAsync(string key) =>
        db.Conversations.Where(c => c.DirectKey == key).Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
