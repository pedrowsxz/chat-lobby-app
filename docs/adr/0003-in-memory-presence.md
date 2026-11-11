# ADR 0003: In-memory presence, no backplane

**Status:** accepted

## Context
"Who is online" changes on every connect and disconnect, potentially many times a minute. Storing it in SQL Server
would mean a write per tab opened or closed, and stale "online" rows after a crash.

## Decision
- `PresenceTracker` is a singleton holding `Dictionary<userId, HashSet<connectionId>>` behind one lock.
  It counts **connections**: a user is online while at least one connection is open (two tabs = two connections).
- `Connect` returns true only on 0 → 1, `Disconnect` only on 1 → 0. Only those transitions are announced.
- On 1 → 0 the hub writes `LastSeenAt` (the only presence data in the database) so the UI can say "visto há 5 min".
  Before announcing "offline" it checks the tracker again: a page navigation opens a new connection while the old one
  is closing, and its "online" must not be overwritten by a late "offline".
- Audience: a registered user's change goes to the `directory` group; a guest's change goes only to their pair.

Deviation from the first plan: one lock over a plain `Dictionary` instead of `ConcurrentDictionary` with a lock per set.
The per-set version has a race (one thread removes the empty set from the dictionary while another adds a connection
to that same set object, which is then lost). Connects are rare enough that one lock costs nothing.

## Consequences / limits
- Works on **one server instance only**. A second instance would have its own dictionary, and `Clients.Users`
  would only reach users connected to the same instance.
- State is lost on restart; clients reconnect within seconds and register again (everyone flickers offline briefly).
- After an abrupt network loss the server notices only when the client timeout expires (~30 s by default).
- Scaling out would need a SignalR backplane (Redis or Azure SignalR Service) and a shared presence store.
  Deliberately not built.
