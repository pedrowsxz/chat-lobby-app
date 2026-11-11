using System.Net;
using Lobby.Tests.Infrastructure;
using Lobby.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class GroupTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task After_leaving_a_group_the_user_can_no_longer_send_or_read()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var groupId = (await Build.Groups(db).CreateAsync(ana.Id, "Leitura", [bia.Id])).ConversationId!.Value;
        var biaClient = fixture.Factory.CreateClientFor(bia.Id);
        Assert.Equal(HttpStatusCode.OK, (await biaClient.GetAsync($"/c/{groupId}")).StatusCode);

        Assert.True(await Build.Groups(db).LeaveAsync(bia.Id, groupId));

        var send = await Build.MessageService(db).SendAsync(bia.Id, groupId, "ainda estou aqui?", Guid.NewGuid());
        Assert.Equal(SendError.NotFound, send.Error);
        Assert.Equal(HttpStatusCode.NotFound, (await biaClient.GetAsync($"/c/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biaClient.GetAsync($"/c/{groupId}/messages?before=1000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await biaClient.GetAsync($"/c/{groupId}/detalhes")).StatusCode);
    }

    [Fact]
    public async Task When_the_last_member_leaves_the_group_and_its_messages_are_deleted()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var groups = Build.Groups(db);
        var groupId = (await groups.CreateAsync(ana.Id, "Temporário", [bia.Id])).ConversationId!.Value;
        await Build.MessageService(db).SendAsync(ana.Id, groupId, "oi", Guid.NewGuid());

        await groups.LeaveAsync(ana.Id, groupId);
        Assert.True(await db.Conversations.AnyAsync(c => c.Id == groupId));
        await groups.LeaveAsync(bia.Id, groupId);

        Assert.False(await db.Conversations.AnyAsync(c => c.Id == groupId));
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == groupId));
    }

    [Fact]
    public async Task Any_member_can_add_people_but_a_non_member_cannot()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var carla = await TestData.CreateUserAsync(db);
        var stranger = await TestData.CreateUserAsync(db);
        var groups = Build.Groups(db);
        var groupId = (await groups.CreateAsync(ana.Id, "Clube", [bia.Id])).ConversationId!.Value;

        Assert.True((await groups.AddMembersAsync(stranger.Id, groupId, [stranger.Id])).NotFound);
        Assert.Null((await groups.AddMembersAsync(bia.Id, groupId, [carla.Id])).Error); // bia is not the creator: no roles

        Assert.Equal(3, await db.ConversationMembers.CountAsync(m => m.ConversationId == groupId));
    }

    [Fact]
    public async Task Group_rules_name_limit_and_sandbox()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var guest = await TestData.CreateUserAsync(db, isGuest: true);
        var groups = Build.Groups(db);
        var many = new List<string>();
        for (var i = 0; i < 20; i++) many.Add((await TestData.CreateUserAsync(db)).Id);

        Assert.NotNull((await groups.CreateAsync(ana.Id, "   ", [bia.Id])).Error);
        Assert.NotNull((await groups.CreateAsync(ana.Id, new string('x', 61), [bia.Id])).Error);
        Assert.NotNull((await groups.CreateAsync(ana.Id, "Grande", many)).Error); // 20 + me = 21
        Assert.Null((await groups.CreateAsync(ana.Id, "Cheio", many.Take(19))).Error); // 19 + me = 20
        Assert.NotNull((await groups.CreateAsync(ana.Id, "Com visitante", [guest.Id])).Error);
        Assert.NotNull((await groups.CreateAsync(guest.Id, "Com usuária", [ana.Id])).Error);
        var botId = await db.Users.Where(u => u.IsDemoBot).Select(u => u.Id).SingleAsync();
        Assert.NotNull((await groups.CreateAsync(ana.Id, "Com o bot", [bia.Id, botId])).Error);
    }
}
