using Lobby.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Services;

// Deletes guests older than 24 hours and everything they created. This is what resets the
// demo: seeded people and the bot can't sign in, so the seeded data itself never changes.
public class GuestCleanup(AppDbContext db, AvatarStorage avatars, TimeProvider time, ILogger<GuestCleanup> logger)
{
    public static readonly TimeSpan GuestLifetime = TimeSpan.FromHours(24);

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = time.GetUtcNow() - GuestLifetime;

        // A pair goes together, once BOTH are older than 24 h. Otherwise the older guest's
        // messages would still sit in the DM created by the younger one.
        var expired = await db.Users
            .Where(u => u.IsGuest && u.CreatedAt < cutoff
                        && !db.Users.Any(pair => pair.Id == u.GuestPairId && pair.CreatedAt >= cutoff))
            .OrderBy(u => u.CreatedAt)
            .Select(u => new { u.Id, u.AvatarFileName })
            .Take(500) // bounded work per run; the next run takes the rest
            .ToListAsync(cancellationToken);
        if (expired.Count == 0) return 0;
        var ids = expired.Select(u => u.Id).ToList();

        // Every FK to a user is Restrict, so the order matters. With EnableRetryOnFailure,
        // a transaction we open ourselves must run inside the execution strategy, so the whole
        // block (not single statements) is what gets retried.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // 1. Conversations they created: the database cascades to their members and messages.
            await db.Conversations.Where(c => ids.Contains(c.CreatedById)).ExecuteDeleteAsync(cancellationToken);
            // 2. Anything of theirs left in other conversations.
            await db.Messages.Where(m => ids.Contains(m.SenderId)).ExecuteDeleteAsync(cancellationToken);
            await db.ConversationMembers.Where(m => ids.Contains(m.UserId)).ExecuteDeleteAsync(cancellationToken);
            // 3. The users (Identity's claim/login/token rows cascade).
            await db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        // 4. Avatar files last: a file delete can't be rolled back, so only after the commit.
        foreach (var user in expired) avatars.Delete(user.AvatarFileName);

        logger.LogInformation("Guest cleanup removed {Count} guests", expired.Count);
        return expired.Count;
    }
}

public class GuestCleanupService(IServiceScopeFactory scopes, TimeProvider time, ILogger<GuestCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GuestCleanup>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Guest cleanup failed; will retry next hour");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
