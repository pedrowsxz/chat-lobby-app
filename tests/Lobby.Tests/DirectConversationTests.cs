using Lobby.Tests.Infrastructure;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class DirectConversationTests(DatabaseFixture fixture)
{
    private static DirectConversationService Service(AppDbContext db) => Build.DirectConversations(db);

    [Fact]
    public async Task Same_pair_returns_the_same_conversation_in_either_order()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);

        var first = await Service(db).GetOrCreateAsync(ana.Id, bia.Id);
        var second = await Service(db).GetOrCreateAsync(bia.Id, ana.Id);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(2, await db.ConversationMembers.CountAsync(m => m.ConversationId == first));
    }

    [Fact]
    public async Task Messaging_yourself_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);

        Assert.Null(await Service(db).GetOrCreateAsync(ana.Id, ana.Id));
        Assert.False(await db.ConversationMembers.AnyAsync(m => m.UserId == ana.Id));
    }

    [Fact]
    public async Task Registered_user_cannot_open_a_chat_with_a_guest_and_vice_versa()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var guest = await TestData.CreateUserAsync(db, isGuest: true);

        Assert.Null(await Service(db).GetOrCreateAsync(ana.Id, guest.Id));
        Assert.Null(await Service(db).GetOrCreateAsync(guest.Id, ana.Id));
    }

    [Fact]
    public async Task Race_where_the_other_insert_wins_returns_the_winning_conversation()
    {
        await using var setup = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(setup);
        var bia = await TestData.CreateUserAsync(setup);
        var competitor = new CompetingDirectChatInterceptor(fixture, ana.Id, bia.Id);
        await using var db = fixture.CreateContext(competitor);

        var id = await Service(db).GetOrCreateAsync(ana.Id, bia.Id);

        Assert.NotNull(competitor.CompetingConversationId);
        Assert.Equal(competitor.CompetingConversationId, id);
        var key = Conversation.MakeDirectKey(ana.Id, bia.Id);
        Assert.Equal(1, await setup.Conversations.CountAsync(c => c.DirectKey == key));
    }

    [Fact]
    public async Task Two_contexts_racing_for_the_same_pair_end_with_exactly_one_conversation()
    {
        for (var round = 0; round < 10; round++)
        {
            await using var setup = fixture.CreateContext();
            var ana = await TestData.CreateUserAsync(setup);
            var bia = await TestData.CreateUserAsync(setup);
            await using var db1 = fixture.CreateContext();
            await using var db2 = fixture.CreateContext();

            var results = await Task.WhenAll(
                Service(db1).GetOrCreateAsync(ana.Id, bia.Id),
                Service(db2).GetOrCreateAsync(bia.Id, ana.Id));

            var key = Conversation.MakeDirectKey(ana.Id, bia.Id);
            var onlyId = await setup.Conversations.Where(c => c.DirectKey == key).Select(c => c.Id).SingleAsync();
            Assert.All(results, id => Assert.Equal(onlyId, id));
        }
    }
}
