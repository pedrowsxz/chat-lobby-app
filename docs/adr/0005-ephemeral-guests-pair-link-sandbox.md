# ADR 0005: Ephemeral guests, pair link and sandbox

**Status:** accepted

## Context
A recruiter will not register two accounts, and alone they can't see anything real-time. The demo must show live
delivery and presence in under a minute, without letting anonymous visitors reach real users (or each other).

## Decision
- **Guest**: `POST /conta/convidado` (10 per IP per hour) creates a user with `IsGuest = true`, no password, an
  `is_guest` claim (added by a custom `UserClaimsPrincipalFactory`, so the hub knows without a query) and a welcome kit:
  a DM with the demo bot and a "Clube do livro (demo)" group with three seeded people and ~120 messages.
- **Pair link**: an unpaired guest sees a single-use, 30-minute link. Opening it in another browser creates a second
  guest already in a DM with the first (`GuestPairId` set on both). The token is spent on **POST** (a button on the
  landing page), not on GET, so link previews in chat apps can't burn it. Single use is enforced by a conditional
  `UPDATE ... WHERE PairToken = @token`: only one request can still find it.
- **Sandbox**: one query, `PeopleService.VisibleTo(viewer)`. A guest sees themself, their pair and the seeded users
  (the bot is seeded). Everyone else sees only non-guests. Every way to start a DM or add a group member goes through it,
  and a guest's presence is sent only to their pair.
- **Demo responder** (demo only, `Demo:SimulateReplies`): saved messages go into a bounded `Channel`; a background loop
  answers DMs with the bot (1–3 s later) and sometimes posts in the demo group, **through the real `MessageService`**.
  The bot has no connection and shows as offline with a "bot" badge.
- **Cleanup**: hourly, guests older than 24 h are deleted with everything they created, in one transaction and in an
  explicit order (conversations they created → their remaining messages and memberships → users), then their avatar files.
  A pair is deleted together once **both** are older than 24 h, otherwise the older guest's messages would still be
  referenced from the DM the younger one created.

## Consequences
- \+ Seeded users and the bot have no password, so seeded data never changes: the demo resets itself.
- \+ Strangers can't message visitors, and visitors can't message real users.
- \- The bot and the seeded users share the per-user rate limit across all visitors. The responder is sequential with a
  1–3 s delay, so it stays under 10 messages per 10 s.
- \- A guest who loses the cookie can't get back in (by design).
