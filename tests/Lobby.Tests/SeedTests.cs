using Lobby.Tests.Infrastructure;
using Lobby.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class SeedTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Seed_creates_demo_people_and_bot_once_and_none_can_sign_in()
    {
        await using var db = fixture.CreateContext();
        await DbInitializer.SeedAsync(db, TimeProvider.System);
        var firstCount = await db.Users.CountAsync(u => u.IsSeeded);

        await DbInitializer.SeedAsync(db, TimeProvider.System);

        Assert.Equal(16, firstCount); // 15 people + the bot
        Assert.Equal(firstCount, await db.Users.CountAsync(u => u.IsSeeded));
        Assert.Equal(1, await db.Users.CountAsync(u => u.IsDemoBot));
        Assert.False(await db.Users.AnyAsync(u => u.IsSeeded && u.PasswordHash != null));
    }

    [Fact]
    public async Task Two_accounts_cannot_share_an_email_but_many_users_can_have_none()
    {
        await using var db = fixture.CreateContext();
        var email = $"dup-{Guid.NewGuid():N}@example.com".ToUpperInvariant();
        var first = await TestData.CreateUserAsync(db);
        var second = await TestData.CreateUserAsync(db);
        await TestData.CreateUserAsync(db); // no e-mail, like every guest: allowed many times

        first.NormalizedEmail = email;
        await db.SaveChangesAsync();
        second.NormalizedEmail = email;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
