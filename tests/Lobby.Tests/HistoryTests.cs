using Lobby.Tests.Infrastructure;
using Lobby.Web.Models;
using Lobby.Web.Services;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class HistoryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Paging_before_returns_older_messages_in_order_without_gaps_or_duplicates_and_after_returns_newer()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        for (var i = 0; i < 75; i++)
            db.Messages.Add(new Message { ConversationId = conversationId, SenderId = i % 2 == 0 ? ana.Id : bia.Id, Body = $"m{i}", SentAt = start.AddMinutes(i), ClientMessageId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var queries = new ConversationQueries(db);

        var seen = new List<string>();
        var page = await queries.GetMessagesAsync(conversationId);
        var pages = 1;
        seen.InsertRange(0, page.Messages.Select(m => m.Body));
        while (page.HasMore)
        {
            page = await queries.GetMessagesAsync(conversationId, before: page.Messages[0].Id);
            seen.InsertRange(0, page.Messages.Select(m => m.Body));
            pages++;
        }

        Assert.Equal(3, pages); // 30 + 30 + 15
        Assert.Equal(Enumerable.Range(0, 75).Select(i => $"m{i}"), seen);

        var all = await queries.GetMessagesAsync(conversationId, before: int.MaxValue);
        var newer = await queries.GetMessagesAsync(conversationId, after: all.Messages[^5].Id);
        Assert.Equal(["m71", "m72", "m73", "m74"], newer.Messages.Select(m => m.Body));
        Assert.False(newer.HasMore);
    }
}
