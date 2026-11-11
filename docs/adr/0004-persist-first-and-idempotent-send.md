# ADR 0004: Persist first, broadcast after commit, idempotent `ClientMessageId`

**Status:** accepted

## Context
Two failure modes matter for a chat:
1. A message shown to others but never saved (it disappears on reload).
2. A message saved twice, when a client resends after a dropped connection without knowing if the first try worked.

## Decision
`MessageService.SendAsync` (used by both the hub and the MVC form):

1. Validate (trimmed body 1–2000), check membership, apply the per-user rate limit.
2. Look up `(SenderId, ClientMessageId)`. If it exists, return that message and do **not** insert or broadcast again.
3. Insert the message and update `Conversation.LastMessageAt` and the sender's `LastReadAt` in **one** `SaveChanges`.
4. **Only after the commit**, read the current members and broadcast. A broadcast failure is logged and swallowed:
   the database is the source of truth, and clients recover through history and the reconnect catch-up.
5. Return the DTO; the hub returns it to the caller, which confirms its optimistic bubble.

The browser generates `ClientMessageId` (`crypto.randomUUID()`). A unique index on `(SenderId, ClientMessageId)`
makes the rule hold even when two copies arrive at the same instant: the loser gets SQL error 2627/2601, which is
caught, and the existing message is returned.

## Consequences
- \+ No ghost messages: if `SaveChanges` throws, the method exits before the broadcast (tested with an interceptor
  that fails the save and a fake broadcaster that must stay empty).
- \+ "Tentar novamente" is always safe, because it reuses the same id.
- \+ A client that reconnects asks for `?after={lastId}` and fills the gap.
- \- The sender's own tabs receive their message twice (hub reply + broadcast); the client dedupes by `ClientMessageId`.
- \- A retry still counts against the rate limit. Acceptable at 10 messages per 10 s.
