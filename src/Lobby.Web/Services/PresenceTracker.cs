namespace Lobby.Web.Services;

// Who is online, in memory. A user is online while they have at least one open
// connection: two tabs = two connections, and closing one tab keeps them online.
//
// Limits (documented in ADR 0003):
// - one server only: a second instance would have its own, different dictionary;
// - lost on restart (clients reconnect and re-register within seconds);
// - after an abrupt network loss the server notices only when the client timeout expires (~30 s);
// - scaling out would need a backplane (e.g. Redis), which this project deliberately doesn't build.
public class PresenceTracker
{
    private readonly Dictionary<string, HashSet<string>> _connections = new();

    // One lock for the whole dictionary. Connects and disconnects are rare compared to
    // messages, so contention is no concern, and one lock rules out the race where one thread
    // removes a user's empty set while another adds a connection to it.
    private readonly Lock _lock = new();

    /// <returns>true if this was the user's first connection (offline → online).</returns>
    public bool Connect(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set))
                _connections[userId] = set = [];
            set.Add(connectionId);
            return set.Count == 1;
        }
    }

    /// <returns>true if this was the user's last connection (online → offline).</returns>
    public bool Disconnect(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set) || !set.Remove(connectionId))
                return false;
            if (set.Count > 0) return false;
            _connections.Remove(userId);
            return true;
        }
    }

    public bool IsOnline(string userId)
    {
        lock (_lock) return _connections.ContainsKey(userId);
    }
}
