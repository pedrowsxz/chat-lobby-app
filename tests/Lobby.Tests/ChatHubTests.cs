using Lobby.Tests.Infrastructure;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Lobby.Tests;

// End-to-end through the real hub, real broadcaster and real database, over an in-memory test server.
[Collection(DatabaseCollection.Name)]
public class ChatHubTests(DatabaseFixture fixture) : IAsyncDisposable
{
    private readonly List<HubConnection> _connections = [];

    private async Task<HubConnection> ConnectAsync(string? userId, Action<MessageDto>? onMessage = null, string? origin = null)
    {
        var server = fixture.Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling; // the test server has no real WebSockets
                if (userId is not null) options.Headers[TestAuthHandler.UserHeader] = userId;
                if (origin is not null) options.Headers["Origin"] = origin;
            })
            .Build();
        if (onMessage is not null) connection.On("messageReceived", onMessage);
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private static async Task<int> CreateGroupAsync(AppDbContext db, params ApplicationUser[] members)
    {
        var conversation = new Conversation
        {
            Type = ConversationType.Group,
            Name = "Grupo de teste",
            CreatedById = members[0].Id,
            CreatedAt = DateTimeOffset.UtcNow,
            Members = members.Select(u => new ConversationMember { UserId = u.Id, JoinedAt = DateTimeOffset.UtcNow }).ToList(),
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    [Fact]
    public async Task Member_receives_a_message_sent_by_another_member()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var received = new TaskCompletionSource<MessageDto>();
        await ConnectAsync(bia.Id, m => received.TrySetResult(m));
        var anaConnection = await ConnectAsync(ana.Id);
        var clientMessageId = Guid.NewGuid();

        var returned = await anaConnection.InvokeAsync<MessageDto>("SendMessage", conversationId, "olá, Bia", clientMessageId);
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("olá, Bia", message.Body);
        Assert.Equal(clientMessageId, message.ClientMessageId);
        Assert.Equal(returned.Id, message.Id);
        Assert.Equal(ana.Id, message.SenderId);
    }

    [Fact]
    public async Task Connected_non_member_receives_nothing()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var stranger = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var biaReceived = new TaskCompletionSource<MessageDto>();
        var strangerReceived = new TaskCompletionSource<MessageDto>();
        await ConnectAsync(stranger.Id, m => strangerReceived.TrySetResult(m));
        await ConnectAsync(bia.Id, m => biaReceived.TrySetResult(m));
        var anaConnection = await ConnectAsync(ana.Id);

        await anaConnection.InvokeAsync<MessageDto>("SendMessage", conversationId, "só para a Bia", Guid.NewGuid());
        await biaReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the broadcast did happen...

        var winner = await Task.WhenAny(strangerReceived.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(strangerReceived.Task, winner); // ...but not to the stranger
    }

    [Fact]
    public async Task Connection_without_authentication_is_rejected()
    {
        var error = await Record.ExceptionAsync(() => ConnectAsync(userId: null));

        Assert.NotNull(error);
        Assert.Contains("401", error.Message);
    }

    [Fact]
    public async Task Connection_from_another_origin_is_dropped_even_with_a_valid_user()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;

        // Ana IS a member, so this send can only fail because the server dropped the connection.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var connection = await ConnectAsync(ana.Id, origin: "https://other-app.example.com");
            await Task.Delay(500); // the hub aborts right after the handshake
            await connection.InvokeAsync<MessageDto>("SendMessage", conversationId, "oi", Guid.NewGuid());
        });
        Assert.False(db.Messages.Any(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task SendMessage_from_a_non_member_throws_HubException_and_saves_nothing()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var stranger = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var strangerConnection = await ConnectAsync(stranger.Id);

        var error = await Assert.ThrowsAsync<HubException>(() =>
            strangerConnection.InvokeAsync<MessageDto>("SendMessage", conversationId, "invasão", Guid.NewGuid()));

        Assert.Contains("Conversa não encontrada", error.Message);
        Assert.False(db.Messages.Any(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task User_added_to_a_group_while_connected_receives_the_next_message_without_reconnecting()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var carla = await TestData.CreateUserAsync(db);
        var groupId = await CreateGroupAsync(db, ana, bia);
        var carlaReceived = new TaskCompletionSource<MessageDto>();
        await ConnectAsync(carla.Id, m => carlaReceived.TrySetResult(m)); // connected BEFORE being added
        var anaConnection = await ConnectAsync(ana.Id);

        db.ConversationMembers.Add(new ConversationMember { ConversationId = groupId, UserId = carla.Id, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await anaConnection.InvokeAsync<MessageDto>("SendMessage", groupId, "bem-vinda, Carla", Guid.NewGuid());

        var message = await carlaReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("bem-vinda, Carla", message.Body);
        Assert.Equal(groupId, message.ConversationId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections) await connection.DisposeAsync();
    }
}
