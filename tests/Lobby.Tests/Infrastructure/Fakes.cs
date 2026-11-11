using Lobby.Tests.Infrastructure;
using Lobby.Web.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lobby.Tests.Infrastructure;

// Makes every SaveChanges on the context fail, as if the database went away mid-request.
public class FailingSaveInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated database failure");
}

// Simulates the DM race deterministically: just before our SaveChanges, "the other user"
// creates the same DM through a second DbContext, so our INSERT hits the unique index.
public class CompetingDirectChatInterceptor(DatabaseFixture fixture, string userA, string userB) : SaveChangesInterceptor
{
    private bool _done;
    public int? CompetingConversationId { get; private set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_done)
        {
            _done = true;
            await using var other = fixture.CreateContext();
            CompetingConversationId = await new DirectConversationService(other, new PeopleService(other), TimeProvider.System)
                .GetOrCreateAsync(userB, userA);
        }
        return result;
    }
}

// Records broadcasts instead of sending them, so tests can assert "exactly one" or "none".
public class FakeBroadcaster : Lobby.Web.Hubs.IChatBroadcaster
{
    public List<(IReadOnlyList<string> MemberIds, Lobby.Web.ViewModels.MessageDto Message)> Calls { get; } = [];

    public Task MessageCreatedAsync(IReadOnlyList<string> memberIds, Lobby.Web.ViewModels.MessageDto message)
    {
        Calls.Add((memberIds, message));
        return Task.CompletedTask;
    }
}
