using Lobby.Web.Data;
using Lobby.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// Who can see whom. This one query is the guest sandbox: every list, search
// and "start a chat" goes through it.
public class PeopleService(AppDbContext db)
{
    public IQueryable<ApplicationUser> VisibleTo(ApplicationUser viewer) =>
        viewer.IsGuest
            // A guest sees only themself, their pair, and the seeded demo people (the bot is seeded too).
            ? db.Users.Where(u => u.Id == viewer.Id || u.Id == viewer.GuestPairId || u.IsSeeded)
            // Everyone else sees everyone who is not a guest.
            : db.Users.Where(u => !u.IsGuest);

    public async Task<bool> CanSeeAsync(string viewerId, string otherUserId)
    {
        var viewer = await db.Users.FindAsync(viewerId);
        return viewer is not null && await VisibleTo(viewer).AnyAsync(u => u.Id == otherUserId);
    }
}
