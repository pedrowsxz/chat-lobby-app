using Lobby.Web.Data;
using Lobby.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// The "welcome kit" every new user gets, so the app is never empty: a DM with the bot and a
// book-club group with three seeded people and ~120 older messages (enough to show "load older").
// Created per user, on demand, never seeded: each visitor gets their own copy, so nobody sees
// another visitor's messages, and guest cleanup can delete it all.
public class DemoKit(AppDbContext db, DirectConversationService directConversations, TimeProvider time)
{
    public const string GroupName = "Clube do livro (demo)";
    private const int GroupMessages = 120;

    public async Task CreateForAsync(ApplicationUser user)
    {
        var now = time.GetUtcNow();
        var random = new Random();

        // DM with the bot, with one unread greeting.
        var bot = await db.Users.SingleAsync(u => u.IsDemoBot);
        var dmId = await directConversations.GetOrCreateAsync(user.Id, bot.Id)
            ?? throw new InvalidOperationException("The bot must be visible to everyone.");
        var dm = await db.Conversations.SingleAsync(c => c.Id == dmId);
        dm.LastMessageAt = now;
        db.Messages.Add(new Message { ConversationId = dmId, SenderId = bot.Id, Body = Data.DemoContent.BotGreeting, SentAt = now, ClientMessageId = Guid.NewGuid() });

        // The group: the user plus the first three seeded people.
        var friends = await db.Users.Where(u => u.IsSeeded && !u.IsDemoBot).OrderBy(u => u.UserName).Take(3).ToListAsync();
        var times = MessageTimes(now, random);
        var group = new Conversation
        {
            Type = ConversationType.Group,
            Name = GroupName,
            CreatedById = user.Id, // so guest cleanup deletes it with the guest
            CreatedAt = times[0].AddMinutes(-5),
            LastMessageAt = times[^1],
            Members = friends.Select(f => new ConversationMember { UserId = f.Id, JoinedAt = times[0] })
                // Everything read except the last 3 messages, so the unread badge shows something.
                .Append(new ConversationMember { UserId = user.Id, JoinedAt = times[0], LastReadAt = times[^4] })
                .ToList(),
            Messages = times.Select(sentAt => new Message
            {
                SenderId = friends[random.Next(friends.Count)].Id,
                Body = Data.DemoContent.ChatLines[random.Next(Data.DemoContent.ChatLines.Length)],
                SentAt = sentAt,
                ClientMessageId = Guid.NewGuid(),
            }).ToList(),
        };
        db.Conversations.Add(group);
        // History is ordered by Id, separators by SentAt; the times above are sorted and EF inserts the
        // batch in list order, so both orders agree (checked on 1 100+ rows; SQL Server doesn't formally promise it).
        await db.SaveChangesAsync();
    }

    // 30 messages a day over the last four days, between 09:00 and ~22:00 (São Paulo).
    // Today's messages end a few minutes ago instead, so none is in the future.
    private static List<DateTimeOffset> MessageTimes(DateTimeOffset now, Random random)
    {
        var times = new List<DateTimeOffset>();
        var todayLocal = Display.ToBrazil(now).Date;
        for (var daysAgo = 3; daysAgo >= 0; daysAgo--)
        {
            for (var i = 0; i < GroupMessages / 4; i++)
            {
                var local = todayLocal.AddDays(-daysAgo).AddHours(9).AddMinutes(i * 26 + random.Next(20));
                var utc = new DateTimeOffset(local, Display.BrazilTime.GetUtcOffset(local)).ToUniversalTime();
                times.Add(daysAgo == 0 ? now.AddMinutes(-(GroupMessages / 4 - i) * 3) : utc);
            }
        }
        return times;
    }
}
