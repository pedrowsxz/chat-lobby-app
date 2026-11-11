using Lobby.Web.Data;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Controllers;

[Route("pessoas")]
public class PeopleController(
    AppDbContext db,
    PeopleService people,
    DirectConversationService directConversations,
    PresenceTracker presence) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q)
    {
        var me = await db.Users.FindAsync(User.GetUserId());
        if (me is null) return NotFound();

        var query = people.VisibleTo(me).Where(u => u.Id != me.Id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u => u.DisplayName.Contains(term) || u.UserName!.Contains(term));
        }

        var list = await query.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new PersonViewModel
            {
                Id = u.Id,
                UserName = u.UserName!,
                DisplayName = u.DisplayName,
                StatusMessage = u.StatusMessage,
                AvatarFileName = u.AvatarFileName,
                IsDemoBot = u.IsDemoBot,
                IsGuest = u.IsGuest,
                LastSeenAt = u.LastSeenAt,
            })
            .Take(200)
            .ToListAsync();
        foreach (var person in list) person.Online = presence.IsOnline(person.Id);

        ViewBag.Query = q;
        // Online first, then alphabetical (already sorted by the query; OrderBy is stable).
        return View(list.OrderByDescending(p => p.Online).ToList());
    }

    // "Enviar mensagem": find or create the DM, then go there.
    [HttpPost("{userId}/conversa")]
    public async Task<IActionResult> StartDirect(string userId)
    {
        var conversationId = await directConversations.GetOrCreateAsync(User.GetUserId(), userId);
        return conversationId is null ? NotFound() : Redirect($"/c/{conversationId}");
    }
}
