using Lobby.Web.Services;

namespace Lobby.Tests;

public class PresenceTrackerTests
{
    [Fact]
    public void User_with_two_connections_stays_online_until_both_close()
    {
        var tracker = new PresenceTracker();

        Assert.True(tracker.Connect("ana", "tab-1"));   // offline -> online: announce
        Assert.False(tracker.Connect("ana", "tab-2"));  // already online: no announcement
        Assert.True(tracker.IsOnline("ana"));

        Assert.False(tracker.Disconnect("ana", "tab-1")); // one tab still open
        Assert.True(tracker.IsOnline("ana"));

        Assert.True(tracker.Disconnect("ana", "tab-2"));  // last one: announce offline
        Assert.False(tracker.IsOnline("ana"));
    }

    [Fact]
    public void Unknown_or_repeated_disconnect_is_ignored()
    {
        var tracker = new PresenceTracker();
        tracker.Connect("ana", "tab-1");

        Assert.False(tracker.Disconnect("bia", "tab-9"));
        Assert.False(tracker.Disconnect("ana", "tab-9"));
        Assert.True(tracker.Disconnect("ana", "tab-1"));
        Assert.False(tracker.Disconnect("ana", "tab-1"));
    }

    [Fact]
    public void Concurrent_connects_and_disconnects_leave_a_consistent_state()
    {
        var tracker = new PresenceTracker();
        Parallel.For(0, 1000, i =>
        {
            tracker.Connect("ana", $"c{i}");
            tracker.Disconnect("ana", $"c{i}");
        });
        Assert.False(tracker.IsOnline("ana"));
    }
}
