using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Controllers;

public class GroupsController(
    AppDbContext db,
    GroupService groups,
    PeopleService people,
    MembershipService membership,
    PresenceTracker presence) : Controller
{
    [HttpGet("/grupos/novo")]
    public async Task<IActionResult> New() =>
        View(new NewGroupViewModel { Candidates = await CandidatesAsync(excludeConversationId: null) });

    [HttpPost("/grupos/novo")]
    public async Task<IActionResult> New(NewGroupViewModel model)
    {
        var result = await groups.CreateAsync(User.GetUserId(), model.Name, model.MemberIds);
        if (result.Error is not null)
        {
            model.Error = result.Error;
            model.Candidates = await CandidatesAsync(excludeConversationId: null);
            return View(model);
        }
        return Redirect($"/c/{result.ConversationId}");
    }

    [HttpGet("/c/{id:int}/detalhes")]
    public async Task<IActionResult> Details(int id)
    {
        if (!await membership.IsMemberAsync(id, User.GetUserId())) return NotFound();
        var name = await db.Conversations.Where(c => c.Id == id && c.Type == ConversationType.Group).Select(c => c.Name).FirstOrDefaultAsync();
        if (name is null) return NotFound();

        var members = await db.ConversationMembers.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.User.DisplayName)
            .Select(m => new PersonViewModel
            {
                Id = m.UserId,
                UserName = m.User.UserName!,
                DisplayName = m.User.DisplayName,
                StatusMessage = m.User.StatusMessage,
                AvatarFileName = m.User.AvatarFileName,
                IsDemoBot = m.User.IsDemoBot,
                IsGuest = m.User.IsGuest,
                LastSeenAt = m.User.LastSeenAt,
            })
            .ToListAsync();
        foreach (var member in members) member.Online = presence.IsOnline(member.Id);

        return View(new GroupDetailsViewModel
        {
            Id = id,
            Name = name,
            Members = members,
            Candidates = await CandidatesAsync(excludeConversationId: id),
            Error = TempData["GroupError"] as string,
        });
    }

    [HttpPost("/c/{id:int}/membros")]
    public async Task<IActionResult> AddMembers(int id, List<string> memberIds)
    {
        var result = await groups.AddMembersAsync(User.GetUserId(), id, memberIds);
        if (result.NotFound) return NotFound();
        if (result.Error is not null) TempData["GroupError"] = result.Error;
        return Redirect($"/c/{id}/detalhes");
    }

    [HttpPost("/c/{id:int}/sair")]
    public async Task<IActionResult> Leave(int id) =>
        await groups.LeaveAsync(User.GetUserId(), id) ? Redirect("/") : NotFound();

    // People I can see, minus myself and minus who is already in the group.
    private async Task<List<PersonViewModel>> CandidatesAsync(int? excludeConversationId)
    {
        var me = await db.Users.FindAsync(User.GetUserId());
        if (me is null) return [];
        var query = people.VisibleTo(me).Where(u => u.Id != me.Id && !u.IsDemoBot);
        if (excludeConversationId is not null)
            query = query.Where(u => !db.ConversationMembers.Any(m => m.ConversationId == excludeConversationId && m.UserId == u.Id));
        return await query.AsNoTracking().OrderBy(u => u.DisplayName)
            .Select(u => new PersonViewModel { Id = u.Id, UserName = u.UserName!, DisplayName = u.DisplayName, AvatarFileName = u.AvatarFileName })
            .Take(200)
            .ToListAsync();
    }
}
