using Lobby.Web.Data;
using Lobby.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

public record GroupResult(int? ConversationId, string? Error, bool NotFound = false);

// Groups have no roles on purpose: any member can add people and anyone can leave.
// A new member sees the whole history (it's the same conversation, nothing is hidden by date).
public class GroupService(AppDbContext db, PeopleService people, MembershipService membership, TimeProvider time)
{
    public async Task<GroupResult> CreateAsync(string creatorId, string? name, IEnumerable<string> memberIds)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > Conversation.NameMaxLength)
            return new(null, "O nome do grupo precisa ter de 1 a 60 caracteres.");

        var others = memberIds.Where(id => id != creatorId).Distinct().ToList();
        if (others.Count == 0)
            return new(null, "Escolha pelo menos uma pessoa.");
        if (others.Count + 1 > Conversation.MaxGroupMembers)
            return new(null, $"Um grupo pode ter no máximo {Conversation.MaxGroupMembers} pessoas.");
        if (!await AllVisibleAsync(creatorId, others))
            return new(null, "Alguma das pessoas escolhidas não está disponível.");

        var now = time.GetUtcNow();
        var group = new Conversation
        {
            Type = ConversationType.Group,
            Name = name,
            CreatedById = creatorId,
            CreatedAt = now,
            Members = others.Append(creatorId).Select(id => new ConversationMember { UserId = id, JoinedAt = now, LastReadAt = now }).ToList(),
        };
        db.Conversations.Add(group);
        await db.SaveChangesAsync();
        return new(group.Id, null);
    }

    public async Task<GroupResult> AddMembersAsync(string userId, int conversationId, IEnumerable<string> memberIds)
    {
        if (!await membership.IsMemberAsync(conversationId, userId) || !await IsGroupAsync(conversationId))
            return new(null, null, NotFound: true);

        var current = await membership.GetMemberIdsAsync(conversationId);
        var newIds = memberIds.Distinct().Except(current).ToList();
        if (newIds.Count == 0)
            return new(conversationId, "Escolha pelo menos uma pessoa.");
        if (current.Count + newIds.Count > Conversation.MaxGroupMembers)
            return new(conversationId, $"Um grupo pode ter no máximo {Conversation.MaxGroupMembers} pessoas.");
        // You can only add people you can see: this keeps guests out of real users' groups and vice versa.
        if (!await AllVisibleAsync(userId, newIds))
            return new(conversationId, "Alguma das pessoas escolhidas não está disponível.");

        var now = time.GetUtcNow();
        db.ConversationMembers.AddRange(newIds.Select(id => new ConversationMember { ConversationId = conversationId, UserId = id, JoinedAt = now }));
        await db.SaveChangesAsync();
        return new(conversationId, null);
    }

    public async Task<bool> LeaveAsync(string userId, int conversationId)
    {
        var removed = await db.ConversationMembers
            .Where(m => m.ConversationId == conversationId && m.UserId == userId && m.Conversation.Type == ConversationType.Group)
            .ExecuteDeleteAsync();
        if (removed == 0) return false;

        // The last one out deletes the group (its messages go with it: cascade).
        if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == conversationId))
            await db.Conversations.Where(c => c.Id == conversationId).ExecuteDeleteAsync();
        return true;
    }

    private Task<bool> IsGroupAsync(int conversationId) =>
        db.Conversations.AnyAsync(c => c.Id == conversationId && c.Type == ConversationType.Group);

    private async Task<bool> AllVisibleAsync(string viewerId, List<string> userIds)
    {
        var viewer = await db.Users.FindAsync(viewerId);
        if (viewer is null) return false;
        // The bot only answers DMs, so it can't be put in a group (the picker hides it too).
        var visible = await people.VisibleTo(viewer).CountAsync(u => userIds.Contains(u.Id) && !u.IsDemoBot);
        return visible == userIds.Count;
    }
}
