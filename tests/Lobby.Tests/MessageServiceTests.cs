using Lobby.Tests.Infrastructure;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class MessageServiceTests(DatabaseFixture fixture)
{
    private readonly FakeBroadcaster _broadcaster = new();

    private MessageService Service(AppDbContext db) => Build.MessageService(db, _broadcaster);

    private async Task<(ApplicationUser Ana, ApplicationUser Bia, int ConversationId)> CreateDirectChatAsync(AppDbContext db)
    {
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var id = await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id);
        return (ana, bia, id!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData(null)]
    public async Task Empty_or_whitespace_body_is_rejected_and_nothing_is_saved(string? body)
    {
        await using var db = fixture.CreateContext();
        var (ana, _, conversationId) = await CreateDirectChatAsync(db);

        var result = await Service(db).SendAsync(ana.Id, conversationId, body, Guid.NewGuid());

        Assert.Equal(SendError.InvalidBody, result.Error);
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Too_long_body_is_rejected_but_exactly_2000_after_trim_is_accepted()
    {
        await using var db = fixture.CreateContext();
        var (ana, _, conversationId) = await CreateDirectChatAsync(db);

        var tooLong = await Service(db).SendAsync(ana.Id, conversationId, new string('a', 2001), Guid.NewGuid());
        var limit = await Service(db).SendAsync(ana.Id, conversationId, "  " + new string('a', 2000) + "  ", Guid.NewGuid());

        Assert.Equal(SendError.InvalidBody, tooLong.Error);
        Assert.Equal(SendError.None, limit.Error);
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Non_member_cannot_send_and_nothing_is_saved()
    {
        await using var db = fixture.CreateContext();
        var (_, _, conversationId) = await CreateDirectChatAsync(db);
        var stranger = await TestData.CreateUserAsync(db);

        var result = await Service(db).SendAsync(stranger.Id, conversationId, "oi", Guid.NewGuid());

        Assert.Equal(SendError.NotFound, result.Error);
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == conversationId));
        Assert.Empty(_broadcaster.Calls);
    }

    [Fact]
    public async Task Same_client_message_id_twice_is_stored_once_and_returns_the_same_message()
    {
        await using var db = fixture.CreateContext();
        var (ana, _, conversationId) = await CreateDirectChatAsync(db);
        var clientMessageId = Guid.NewGuid();

        var first = await Service(db).SendAsync(ana.Id, conversationId, "oi", clientMessageId);
        var second = await Service(db).SendAsync(ana.Id, conversationId, "oi", clientMessageId);

        Assert.Equal(first.Message!.Id, second.Message!.Id);
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ConversationId == conversationId));
        Assert.Single(_broadcaster.Calls);
    }

    [Fact]
    public async Task Reusing_a_client_message_id_in_another_conversation_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var (ana, _, first) = await CreateDirectChatAsync(db);
        var carla = await TestData.CreateUserAsync(db);
        var second = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, carla.Id))!.Value;
        var clientMessageId = Guid.NewGuid();

        await Service(db).SendAsync(ana.Id, first, "na primeira", clientMessageId);
        var reused = await Service(db).SendAsync(ana.Id, second, "na segunda", clientMessageId);

        Assert.Equal(SendError.InvalidBody, reused.Error);
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == second));
    }

    [Fact]
    public async Task Broadcast_goes_to_all_current_members_after_the_save()
    {
        await using var db = fixture.CreateContext();
        var (ana, bia, conversationId) = await CreateDirectChatAsync(db);

        var result = await Service(db).SendAsync(ana.Id, conversationId, "oi", Guid.NewGuid());

        var call = Assert.Single(_broadcaster.Calls);
        Assert.Equal(new[] { ana.Id, bia.Id }.Order(), call.MemberIds.Order());
        Assert.Equal(result.Message!.Id, call.Message.Id);
        Assert.Equal(ConversationType.Direct, call.Message.ConversationType);
    }

    [Fact]
    public async Task When_SaveChanges_fails_nothing_is_broadcast()
    {
        await using var setup = fixture.CreateContext();
        var (ana, _, conversationId) = await CreateDirectChatAsync(setup);
        await using var failing = fixture.CreateContext(new FailingSaveInterceptor());

        await Assert.ThrowsAnyAsync<Exception>(() => Service(failing).SendAsync(ana.Id, conversationId, "oi", Guid.NewGuid()));

        Assert.Empty(_broadcaster.Calls);
        Assert.False(await setup.Messages.AnyAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Rate_limit_rejects_the_eleventh_message_in_ten_seconds()
    {
        await using var db = fixture.CreateContext();
        var (ana, _, conversationId) = await CreateDirectChatAsync(db);
        var service = Build.MessageService(db, _broadcaster, Build.RateLimiter(10));

        for (var i = 0; i < 10; i++)
            Assert.Equal(SendError.None, (await service.SendAsync(ana.Id, conversationId, $"m{i}", Guid.NewGuid())).Error);
        var eleventh = await service.SendAsync(ana.Id, conversationId, "spam", Guid.NewGuid());

        Assert.Equal(SendError.RateLimited, eleventh.Error);
        Assert.Equal(10, await db.Messages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Send_trims_the_body_and_updates_last_message_and_my_last_read()
    {
        await using var db = fixture.CreateContext();
        var (ana, bia, conversationId) = await CreateDirectChatAsync(db);

        var result = await Service(db).SendAsync(ana.Id, conversationId, "  olá  ", Guid.NewGuid());

        Assert.Equal("olá", result.Message!.Body);
        await using var check = fixture.CreateContext();
        var conversation = await check.Conversations.Include(c => c.Members).SingleAsync(c => c.Id == conversationId);
        Assert.Equal(result.Message.SentAt, conversation.LastMessageAt);
        Assert.Equal(result.Message.SentAt, conversation.Members.Single(m => m.UserId == ana.Id).LastReadAt);
        Assert.Null(conversation.Members.Single(m => m.UserId == bia.Id).LastReadAt);
    }
}
