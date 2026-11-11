using System.Net;
using Lobby.Tests.Infrastructure;
using Lobby.Web.Services;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class MembershipHttpTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Non_member_gets_404_on_the_page_and_on_the_history_endpoint()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var stranger = await TestData.CreateUserAsync(db);
        var conversationId = await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id);
        var member = fixture.Factory.CreateClientFor(ana.Id);
        var outsider = fixture.Factory.CreateClientFor(stranger.Id);

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/c/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/c/{conversationId}/messages?before=1000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/c/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/c/{conversationId}/messages?before=1000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/c/{conversationId}/messages?after=0")).StatusCode);
        // Same answer as for a conversation that doesn't exist: no way to probe ids.
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/c/999999")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_login()
    {
        var anonymous = fixture.Factory.CreateClientFor(null);

        var response = await anonymous.GetAsync("/c/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // the test scheme challenges with 401
    }
}
