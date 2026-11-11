using Lobby.Web.Data;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Controllers;

public class ChatController(
    AppDbContext db,
    MembershipService membership,
    ConversationQueries queries,
    MessageService messages,
    PresenceTracker presence,
    GuestService guests,
    TimeProvider time) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index() => View(await BuildPageAsync(null, null));

    [HttpGet("/c/{id:int}")]
    public async Task<IActionResult> Conversation(int id, int? before)
    {
        // Not a member? 404, the same answer as for an id that doesn't exist.
        if (!await membership.IsMemberAsync(id, User.GetUserId())) return NotFound();
        return View("Index", await BuildPageAsync(id, before));
    }

    // "Load older" (?before=) and reconnect catch-up (?after=). Returns HTML, not JSON:
    // Razor encodes the message text, and the browser just inserts the fragment.
    [HttpGet("/c/{id:int}/messages")]
    public async Task<IActionResult> Messages(int id, int? before, int? after)
    {
        if (!await membership.IsMemberAsync(id, User.GetUserId())) return NotFound();
        var page = await queries.GetMessagesAsync(id, before, after);
        Response.Headers["X-Has-More"] = page.HasMore ? "true" : "false";
        return PartialView("_Messages", new MessageListModel(page.Messages, User.GetUserId(), time.GetUtcNow()));
    }

    // The no-JavaScript fallback: a normal form POST, then redirect (Post/Redirect/Get),
    // so pressing F5 doesn't resend. Same SendAsync as the hub.
    [HttpPost("/c/{id:int}/messages")]
    public async Task<IActionResult> Send(int id, string? body, Guid clientMessageId)
    {
        var result = await messages.SendAsync(User.GetUserId(), id, body, clientMessageId);
        switch (result.Error)
        {
            case SendError.NotFound:
                return NotFound();
            case SendError.InvalidBody:
                TempData["SendError"] = "A mensagem precisa ter de 1 a 2000 caracteres.";
                break;
            case SendError.RateLimited:
                TempData["SendError"] = "Você está enviando rápido demais. Espere alguns segundos.";
                break;
        }
        return Redirect($"/c/{id}#composer");
    }

    // Called by chat.js when a conversation is opened, and when a message arrives while it is open.
    // The WHERE includes my user id, so this only ever touches my own membership row.
    [HttpPost("/c/{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var now = time.GetUtcNow();
        var updated = await db.ConversationMembers
            .Where(m => m.ConversationId == id && m.UserId == User.GetUserId())
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastReadAt, now));
        return updated == 0 ? NotFound() : NoContent();
    }

    // Sidebar HTML, re-fetched after a reconnect or when a message arrives for a conversation
    // this page has never shown.
    [HttpGet("/c/lista")]
    public async Task<IActionResult> Sidebar([FromQuery] int? active)
    {
        var items = await LoadSidebarAsync();
        return PartialView("_Sidebar", new SidebarModel(items, active, time.GetUtcNow()));
    }

    private async Task<ChatPageViewModel> BuildPageAsync(int? id, int? before)
    {
        var userId = User.GetUserId();
        var header = id is null ? null : await queries.GetHeaderAsync(id.Value, userId);
        if (header?.Other is not null) header.Online = presence.IsOnline(header.Other.Id);

        // Unpaired guests get the "open this link in another window" card.
        var pairToken = User.IsGuest() ? await guests.GetOrIssuePairTokenAsync(userId) : null;

        return new ChatPageViewModel
        {
            Conversations = await LoadSidebarAsync(),
            ActiveConversationId = id,
            Header = header,
            Messages = id is null ? null : await queries.GetMessagesAsync(id.Value, before),
            PairLink = pairToken is null ? null : $"{Request.Scheme}://{Request.Host}/par/{pairToken}",
        };
    }

    private async Task<List<ConversationListItem>> LoadSidebarAsync()
    {
        var items = await queries.GetSidebarAsync(User.GetUserId());
        foreach (var item in items)
            if (item.Other is not null) item.Online = presence.IsOnline(item.Other.Id);
        return items;
    }
}
