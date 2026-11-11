using System.Threading.RateLimiting;
using Lobby.Web.Data;
using Lobby.Web.Hubs;
using Lobby.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lobby.Tests.Infrastructure;

// Hand-wired services for database tests (no DI container, no mocking library).
public static class Build
{
    public static PartitionedRateLimiter<string> RateLimiter(int permits = 10) =>
        PartitionedRateLimiter.Create<string, string>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromSeconds(10) }));

    public static MessageService MessageService(AppDbContext db, IChatBroadcaster? broadcaster = null, PartitionedRateLimiter<string>? limiter = null) =>
        new(db, new MembershipService(db), broadcaster ?? new FakeBroadcaster(), limiter ?? RateLimiter(1000), new DemoReplyQueue(enabled: false), TimeProvider.System, NullLogger<MessageService>.Instance);

    public static DirectConversationService DirectConversations(AppDbContext db, TimeProvider? time = null) =>
        new(db, new PeopleService(db), time ?? TimeProvider.System);

    public static GroupService Groups(AppDbContext db) =>
        new(db, new PeopleService(db), new MembershipService(db), TimeProvider.System);
}
