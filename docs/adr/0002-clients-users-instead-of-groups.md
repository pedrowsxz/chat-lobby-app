# ADR 0002: `Clients.Users(memberIds)` instead of one SignalR group per conversation

**Status:** accepted (replaces the original "one group per conversation" plan)

## Context
The usual SignalR tutorial puts each chat room in a group: join the group on connect, send with
`Clients.Group(roomId)`. But groups hold **connections**, not users, and nothing updates them when membership changes:

- A user who is already connected and gets added to a new DM or group is not in that SignalR group until they reconnect,
  so they miss messages.
- A member who leaves a group **keeps receiving its messages** until they reconnect. That is a privacy bug.

Fixing it means tracking every connection of every user and patching group membership on every join and leave,
across all their tabs.

## Decision
On every send, `MessageService` reads the conversation's **current** member ids from the database, after the commit,
and calls `IChatBroadcaster.MessageCreatedAsync(memberIds, dto)`, which does `Clients.Users(memberIds)`.
SignalR maps a user id (the `NameIdentifier` claim) to all of that user's live connections.

SignalR groups are still used where the audience is stable and can't be wrong in a harmful way: the `directory`
group for presence (all connections of non-guest users).

## Consequences
- \+ Membership is always correct: added → receives the next message; left → receives nothing. The integration test
  `User_added_to_a_group_while_connected_receives_the_next_message_without_reconnecting` proves it.
- \+ One rule for authorization and delivery: the `ConversationMembers` table.
- \- One extra indexed query per message (`SELECT UserId FROM ConversationMembers WHERE ConversationId = @id`).
  For a chat this is negligible.
- \- `Clients.Users` with a long list is less efficient than one group for very large rooms; groups here are capped at 20.
