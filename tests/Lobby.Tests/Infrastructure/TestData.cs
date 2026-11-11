using Lobby.Web.Data;
using Lobby.Web.Models;

namespace Lobby.Tests.Infrastructure;

public static class TestData
{
    public static async Task<ApplicationUser> CreateUserAsync(
        AppDbContext db, bool isGuest = false, bool isSeeded = false, string? pairId = null, DateTimeOffset? createdAt = null)
    {
        var handle = (isGuest ? "convidado-" : "user-") + Guid.NewGuid().ToString("N")[..12];
        var user = new ApplicationUser
        {
            UserName = handle,
            NormalizedUserName = handle.ToUpperInvariant(),
            DisplayName = "Teste " + handle[^4..],
            IsGuest = isGuest,
            IsSeeded = isSeeded,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
