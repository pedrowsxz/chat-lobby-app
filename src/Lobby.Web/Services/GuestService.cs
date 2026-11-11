using System.Security.Cryptography;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

public record PairResult(ApplicationUser Guest, int ConversationId);

public class GuestService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    DirectConversationService directConversations,
    TimeProvider time)
{
    public static readonly TimeSpan PairTokenLifetime = TimeSpan.FromMinutes(30);

    // A guest has no password at all, the same effect as a random password nobody
    // knows: the only way in is the cookie we issue right now.
    public async Task<ApplicationUser> CreateGuestAsync(string? pairId = null)
    {
        string code;
        do
        {
            code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2));
        } while (await db.Users.AnyAsync(u => u.UserName == "convidado-" + code));

        var guest = new ApplicationUser
        {
            UserName = "convidado-" + code,
            DisplayName = "Visitante " + code.ToUpperInvariant(),
            IsGuest = true,
            GuestPairId = pairId,
            CreatedAt = time.GetUtcNow(),
        };
        var result = await userManager.CreateAsync(guest);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create guest: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        return guest;
    }

    // The pair card always shows a valid link: reuse the token while it is fresh, otherwise issue a new one.
    public async Task<string?> GetOrIssuePairTokenAsync(string guestId)
    {
        var guest = await db.Users.FindAsync(guestId);
        if (guest is null || !guest.IsGuest || guest.GuestPairId is not null) return null;

        if (guest.PairToken is null || guest.PairTokenExpiresAt <= time.GetUtcNow())
        {
            guest.PairToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24)); // 192 bits, unguessable
            guest.PairTokenExpiresAt = time.GetUtcNow() + PairTokenLifetime;
            await db.SaveChangesAsync();
        }
        return guest.PairToken;
    }

    // For the landing page: who invited me? Null if the link is unknown, expired, or already used.
    public Task<ApplicationUser?> FindInviterAsync(string token)
    {
        var now = time.GetUtcNow();
        return db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PairToken == token && u.PairTokenExpiresAt > now && u.GuestPairId == null);
    }

    public async Task<PairResult?> AcceptPairAsync(string token)
    {
        var inviter = await FindInviterAsync(token);
        if (inviter is null) return null;

        // Single use, even with two clicks at the same moment: only one UPDATE can still find the
        // token in the row; the other updates 0 rows and gets "link already used".
        var claimed = await db.Users
            .Where(u => u.Id == inviter.Id && u.PairToken == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.PairToken, (string?)null)
                .SetProperty(u => u.PairTokenExpiresAt, (DateTimeOffset?)null));
        if (claimed == 0) return null;

        var second = await CreateGuestAsync(pairId: inviter.Id);
        await db.Users.Where(u => u.Id == inviter.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.GuestPairId, second.Id));

        // Now each can see the other (PeopleService allows "my pair"), so the DM is allowed.
        var conversationId = await directConversations.GetOrCreateAsync(second.Id, inviter.Id)
            ?? throw new InvalidOperationException("Paired guests must see each other.");
        return new PairResult(second, conversationId);
    }
}
