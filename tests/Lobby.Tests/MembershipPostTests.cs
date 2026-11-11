using System.Net;
using System.Text.RegularExpressions;
using Lobby.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Tests;

// The write paths over HTTP: antiforgery is enforced, and non-members get 404 on every POST.
[Collection(DatabaseCollection.Name)]
public partial class MembershipPostTests(DatabaseFixture fixture)
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    // Like a browser: load a page (sets the antiforgery cookie on this client), read the hidden token.
    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/pessoas");
        return TokenRegex().Match(html).Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string? token, params (string, string)[] fields)
    {
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).ToList();
        if (token is not null) form.Add(new("__RequestVerificationToken", token));
        return client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected_even_for_a_member()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var client = fixture.Factory.CreateClientFor(ana.Id);

        var response = await PostAsync(client, $"/c/{conversationId}/messages", token: null, ("body", "oi"), ("clientMessageId", Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Member_can_send_with_the_form_and_is_redirected()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var conversationId = (await Build.DirectConversations(db).GetOrCreateAsync(ana.Id, bia.Id))!.Value;
        var client = fixture.Factory.CreateClientFor(ana.Id);
        var token = await GetTokenAsync(client);

        var response = await PostAsync(client, $"/c/{conversationId}/messages", token, ("body", "oi pelo formulário"), ("clientMessageId", Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await db.Messages.AnyAsync(m => m.ConversationId == conversationId && m.Body == "oi pelo formulário"));
    }

    [Fact]
    public async Task Non_member_gets_404_on_every_conversation_post()
    {
        await using var db = fixture.CreateContext();
        var ana = await TestData.CreateUserAsync(db);
        var bia = await TestData.CreateUserAsync(db);
        var stranger = await TestData.CreateUserAsync(db);
        var groupId = (await Build.Groups(db).CreateAsync(ana.Id, "Privado", [bia.Id])).ConversationId!.Value;
        var client = fixture.Factory.CreateClientFor(stranger.Id);
        var token = await GetTokenAsync(client);

        var send = await PostAsync(client, $"/c/{groupId}/messages", token, ("body", "oi"), ("clientMessageId", Guid.NewGuid().ToString()));
        var read = await PostAsync(client, $"/c/{groupId}/read", token);
        var add = await PostAsync(client, $"/c/{groupId}/membros", token, ("memberIds", stranger.Id));
        var leave = await PostAsync(client, $"/c/{groupId}/sair", token);

        Assert.All([send, read, add, leave], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == groupId));
        Assert.Equal(2, await db.ConversationMembers.CountAsync(m => m.ConversationId == groupId));
    }
}
