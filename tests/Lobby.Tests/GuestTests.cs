using Lobby.Tests.Infrastructure;
using Lobby.Web.Data;
using Lobby.Web.Models;
using Lobby.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Lobby.Tests;

[Collection(DatabaseCollection.Name)]
public class GuestTests(DatabaseFixture fixture)
{
    private GuestService Guests(IServiceScope scope, TimeProvider time)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new GuestService(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), Build.DirectConversations(db, time), time);
    }

    [Fact]
    public async Task Sandbox_hides_guests_from_registered_users_and_from_guests_who_are_not_their_pair()
    {
        await using var db = fixture.CreateContext();
        var registered = await TestData.CreateUserAsync(db);
        var otherRegistered = await TestData.CreateUserAsync(db);
        var guestA = await TestData.CreateUserAsync(db, isGuest: true);
        var guestB = await TestData.CreateUserAsync(db, isGuest: true, pairId: guestA.Id);
        guestA.GuestPairId = guestB.Id;
        var loner = await TestData.CreateUserAsync(db, isGuest: true);
        await db.SaveChangesAsync();
        var people = new PeopleService(db);

        var seenByRegistered = await people.VisibleTo(registered).Select(u => u.Id).ToListAsync();
        var seenByGuestA = await people.VisibleTo(guestA).Select(u => u.Id).ToListAsync();
        var seenByLoner = await people.VisibleTo(loner).Select(u => u.Id).ToListAsync();

        Assert.Contains(otherRegistered.Id, seenByRegistered);
        Assert.DoesNotContain(guestA.Id, seenByRegistered);
        Assert.DoesNotContain(loner.Id, seenByRegistered);

        Assert.Contains(guestB.Id, seenByGuestA);              // my pair
        Assert.DoesNotContain(loner.Id, seenByGuestA);         // another guest
        Assert.DoesNotContain(registered.Id, seenByGuestA);    // real users
        Assert.True(await db.Users.Where(u => seenByGuestA.Contains(u.Id)).AllAsync(u => u.IsSeeded || u.Id == guestA.Id || u.Id == guestB.Id));
        Assert.True(await db.Users.AnyAsync(u => u.IsDemoBot && seenByGuestA.Contains(u.Id)));

        Assert.DoesNotContain(guestA.Id, seenByLoner);
    }

    [Fact]
    public async Task Pair_token_works_once_and_creates_a_paired_guest_with_a_shared_dm()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var guests = Guests(scope, TimeProvider.System);
        var inviter = await guests.CreateGuestAsync();
        var token = (await guests.GetOrIssuePairTokenAsync(inviter.Id))!;

        var pair = await guests.AcceptPairAsync(token);
        var second = await guests.AcceptPairAsync(token);

        Assert.NotNull(pair);
        Assert.Null(second);
        await using var db = fixture.CreateContext();
        var inviterRow = await db.Users.SingleAsync(u => u.Id == inviter.Id);
        Assert.Equal(pair.Guest.Id, inviterRow.GuestPairId);
        Assert.Equal(inviter.Id, pair.Guest.GuestPairId);
        Assert.Null(inviterRow.PairToken);
        Assert.Equal(2, await db.ConversationMembers.CountAsync(m => m.ConversationId == pair.ConversationId && (m.UserId == inviter.Id || m.UserId == pair.Guest.Id)));
        using var nextRequest = fixture.Factory.Services.CreateScope(); // ExecuteUpdate bypasses the old context's tracked copy
        Assert.Null(await Guests(nextRequest, TimeProvider.System).GetOrIssuePairTokenAsync(inviter.Id)); // paired: no new link
    }

    [Fact]
    public async Task Pair_token_expires_after_30_minutes()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var guests = Guests(scope, time);
        var inviter = await guests.CreateGuestAsync();
        var token = (await guests.GetOrIssuePairTokenAsync(inviter.Id))!;

        time.Advance(TimeSpan.FromMinutes(31));

        Assert.Null(await guests.FindInviterAsync(token));
        Assert.Null(await guests.AcceptPairAsync(token));
        Assert.NotEqual(token, await guests.GetOrIssuePairTokenAsync(inviter.Id)); // the card shows a fresh link
    }

    [Fact]
    public async Task Cleanup_deletes_a_25_hour_old_guest_with_everything_and_keeps_a_fresh_one()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var avatarDir = Path.Combine(Path.GetTempPath(), "lobby-cleanup-" + Guid.NewGuid().ToString("N"));
        var avatars = new AvatarStorage(avatarDir);
        await using var db = fixture.CreateContext();
        var kit = new DemoKit(db, Build.DirectConversations(db), time);

        var old = await TestData.CreateUserAsync(db, isGuest: true, createdAt: time.GetUtcNow().AddHours(-25));
        var fresh = await TestData.CreateUserAsync(db, isGuest: true, createdAt: time.GetUtcNow().AddHours(-1));
        await kit.CreateForAsync(old);   // DM with the bot + group with seeded people and 120 messages
        await kit.CreateForAsync(fresh);
        var oldGroupId = await db.Conversations.Where(c => c.CreatedById == old.Id && c.Type == ConversationType.Group).Select(c => c.Id).SingleAsync();
        await Build.MessageService(db).SendAsync(old.Id, oldGroupId, "tchau", Guid.NewGuid());
        old.AvatarFileName = "0123456789abcdef0123456789abcdef.png";
        await File.WriteAllBytesAsync(Path.Combine(avatarDir, old.AvatarFileName), [0x89, 0x50]);
        await db.SaveChangesAsync();

        // Run only for these two guests' sake, but the method sees the whole table: other tests'
        // guests are recent, so they are not affected.
        var removed = await new GuestCleanup(db, avatars, time, NullLogger<GuestCleanup>.Instance).RunAsync();

        Assert.True(removed >= 1);
        await using var check = fixture.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == old.Id));
        Assert.False(await check.Conversations.AnyAsync(c => c.CreatedById == old.Id));
        Assert.False(await check.Messages.AnyAsync(m => m.SenderId == old.Id));
        Assert.False(await check.ConversationMembers.AnyAsync(m => m.UserId == old.Id));
        Assert.False(File.Exists(Path.Combine(avatarDir, "0123456789abcdef0123456789abcdef.png")));

        Assert.True(await check.Users.AnyAsync(u => u.Id == fresh.Id));
        Assert.Equal(2, await check.Conversations.CountAsync(c => c.CreatedById == fresh.Id));
        Assert.Equal(16, await check.Users.CountAsync(u => u.IsSeeded)); // seeded data untouched
    }

    [Fact]
    public async Task Cleanup_keeps_an_old_guest_while_their_pair_is_still_young()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = fixture.CreateContext();
        var older = await TestData.CreateUserAsync(db, isGuest: true, createdAt: time.GetUtcNow().AddHours(-24.2));
        var younger = await TestData.CreateUserAsync(db, isGuest: true, pairId: older.Id, createdAt: time.GetUtcNow().AddHours(-23.8));
        older.GuestPairId = younger.Id;
        await db.SaveChangesAsync();
        var cleanup = new GuestCleanup(db, new AvatarStorage(Path.GetTempPath()), time, NullLogger<GuestCleanup>.Instance);

        await cleanup.RunAsync();
        Assert.True(await db.Users.AnyAsync(u => u.Id == older.Id));

        time.Advance(TimeSpan.FromHours(1));
        await cleanup.RunAsync();
        Assert.False(await db.Users.AnyAsync(u => u.Id == older.Id || u.Id == younger.Id));
    }

    [Fact]
    public async Task Welcome_kit_has_a_bot_dm_and_a_group_with_older_messages_and_a_few_unread()
    {
        await using var db = fixture.CreateContext();
        var user = await TestData.CreateUserAsync(db);

        await new DemoKit(db, Build.DirectConversations(db), TimeProvider.System).CreateForAsync(user);

        var sidebar = await new ConversationQueries(db).GetSidebarAsync(user.Id);
        var group = Assert.Single(sidebar, c => c.Type == ConversationType.Group);
        var dm = Assert.Single(sidebar, c => c.Type == ConversationType.Direct);
        Assert.Equal(DemoKit.GroupName, group.Title);
        Assert.Equal(3, group.UnreadCount);
        Assert.Equal(1, dm.UnreadCount);
        Assert.True(dm.Other!.IsDemoBot);
        Assert.Equal(120, await db.Messages.CountAsync(m => m.ConversationId == group.Id));
        Assert.True(await db.Messages.Where(m => m.ConversationId == group.Id).AllAsync(m => m.SentAt <= DateTimeOffset.UtcNow));
        var firstPage = await new ConversationQueries(db).GetMessagesAsync(group.Id);
        Assert.True(firstPage.HasMore);
    }
}
