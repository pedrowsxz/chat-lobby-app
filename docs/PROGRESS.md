# Progress

Each stage ends with: `dotnet build` (no warnings), `dotnet test` green, `docker compose up --build` working,
manual checks with curl, and one commit `stage N: ...`.

## Stage 1: Identity and profile ✅

- Solution `Lobby.slnx` with `src/Lobby.Web` (MVC) and `tests/Lobby.Tests` (xUnit v3).
- Packages pinned to the latest .NET 10 versions (EF Core / Identity / SignalR client 10.0.15). .NET 11 packages exist but target net11 only.
- `global.json` pins SDK 10.0.1xx+ and opts into Microsoft Testing Platform (required by xunit.v3 4.x for `dotnet test`).
- `dotnet-ef` 10.0.15 as a local tool (`dotnet tool restore`).
- `ApplicationUser` with DisplayName, StatusMessage, AvatarFileName, IsGuest, IsSeeded, IsDemoBot, CreatedAt, LastSeenAt.
- `IdentityUserContext` (no role tables), custom `AccountController`: register, login (user name or e-mail, lockout), logout, guest.
- `is_guest`, `display_name` and `avatar` claims through `LobbyClaimsPrincipalFactory`.
- Fallback policy = authenticated; global `AutoValidateAntiforgeryToken`; `$.ajaxSetup` sends the token header.
- Guest creation rate-limited per IP (`guest` policy, 10/hour).
- Profile edit, change password (not for guests), avatar upload with extension + content type + magic bytes checks,
  guid file names, storage in `App:DataPath/avatars` (Docker volume `appdata`), `GET /avatars/{name}` with name validation and nosniff.
- Initials avatars with a stable per-user colour. Layout and design (Bootstrap 5.3, one accent colour, follows OS dark mode).
- Deterministic Bogus seed: 15 pt-BR people + "Assistente Lobby" bot, no passwords.
- Migrations applied at startup with a retry loop; `EnableRetryOnFailure`.
- Dockerfile (multi-stage, non-root), `compose.yaml` (SQL Server 2022 with healthcheck + web), `.env.example`.
- Tests: avatar validation (fake PNG, oversize, SVG, content-type mismatch, guid name, endpoint name validation) and seed idempotency.
- Verified with curl: guest sign-in, all four upload cases, avatar headers, traversal → 404, register/login/logout.

## Stage 2: Conversations and messages over plain MVC ✅

- Entities `Conversation`, `ConversationMember` (composite key, JoinedAt, LastReadAt), `Message`; enum stored as string;
  filtered unique index on `DirectKey`, check constraint on Type/DirectKey/Name, unique `(SenderId, ClientMessageId)`,
  index `(ConversationId, Id)`; Cascade only from Conversation, Restrict on every FK to a user. `GuestPairId` added to users.
- `MembershipService.IsMemberAsync`: used by the page, history endpoint, MVC send and (via `MessageService`) the hub.
- `PeopleService.VisibleTo`: the guest sandbox query.
- `DirectConversationService.GetOrCreateAsync` with the unique-index race handled (2627/2601 → detach → re-query).
- `MessageService.SendAsync`: trim + 1–2000, membership, idempotency by `ClientMessageId`, persist first.
- `ConversationQueries`: sidebar (one projected query with preview, unread count, other person), header, keyset history.
- Pages: `/` and `/c/{id}` (two panes, mobile single pane), `/c/{id}/messages?before|after` partial, PRG send form,
  `/c/{id}/read`, `/c/lista` sidebar partial, `/pessoas` with search and "Enviar mensagem".
- `chat.js`: load older on scroll with anchored position, day separators, mark read, composer (Enter / Shift+Enter, counter).
- `PresenceTracker` (pure class; wired to the hub in stage 3). Deviation: one lock over a plain Dictionary instead of
  ConcurrentDictionary + per-set locks (that version has a remove/add race).
- Tests: DM same pair / self / sandbox / deterministic race / concurrent race ×10, membership 404 over HTTP,
  send validation / non-member / idempotency / side effects, history paging, presence tracker.

## Stage 3: SignalR ✅

- `ChatHub` (`[Authorize]`, `/hubs/chat`): `SendMessage` → `MessageService.SendAsync`, failures as short pt-BR `HubException`s;
  `EnableDetailedErrors` only in Development.
- `MessageService`: per-user `PartitionedRateLimiter` (10 per 10 s, singleton), broadcast only after the commit to
  `Clients.Users(memberIds)` through `IChatBroadcaster`; broadcast failures are logged and swallowed.
- Presence: `PresenceTracker` connection counting, `directory` group for non-guests, guests announce only to their pair,
  `LastSeenAt` written when the last connection closes.
- `chat.js`: `withAutomaticReconnect` + banner, optimistic "enviando…" bubble, retry with the same `clientMessageId`,
  dedupe by `clientMessageId`, auto-scroll only near the bottom (else "novas mensagens ↓"), sidebar update / refresh,
  catch-up with `?after=` on reconnect, presence dots and texts. Falls back to the form POST when not connected.
- No CORS, cookie `SameSite=Lax`, anonymous negotiate → 401.
- Tests: SignalR integration (member receives, non-member receives nothing in 1 s, unauthenticated rejected,
  non-member `HubException`, user added while connected receives next message), broadcast-once on idempotent send,
  no broadcast when `SaveChanges` fails, rate limit.
- Verified in a real headless Chromium (Playwright, dev tool only, not in the repo): WebSocket transport negotiated,
  two users chat live, second tab of the same user shows no duplicate, `<img onerror>` rendered as text,
  reconnect banner + catch-up after a dropped socket, presence goes offline only after both tabs close.

## Stage 4: Groups, guest demo, CI, deploy and docs ✅

- Groups: create (name 1–60, up to 20 people, only people you can see), any member adds, anyone leaves,
  last one out deletes the group. Pages "Novo grupo" and group details.
- Guests: welcome kit (`DemoKit`: bot DM + "Clube do livro (demo)" with 120 messages over 4 days, 3 unread),
  pair link (single use by conditional UPDATE, 30 min, spent on POST), sandbox, demo responder (bounded Channel +
  BackgroundService, replies through `MessageService`), hourly cleanup (pairs deleted together, explicit delete order in
  one transaction inside the execution strategy, avatar files after commit).
- Registered users get the same welcome kit.
- Presence fix found in the browser test: a late "offline" from the old page could overwrite the "online" from the new
  page; the hub now re-checks the tracker before announcing offline.
- CI (`.github/workflows/ci.yml`), manual SSH deploy workflow, `compose.prod.yaml` + `Caddyfile` (standalone),
  `deploy/compose.shared.yaml` (shared VPS), `docs/DEPLOY.md`. Workflows pass actionlint.
- Verified through Caddy locally (`LOBBY_DOMAIN=localhost`): browser uses `wss://` only, auth cookie is `Secure`,
  pair link is `https://`.
- Docs: README (pt-BR, Mermaid ER + sequence, Reflexão left for the author), ADRs 0001–0006, STUDY_GUIDE (pt-BR),
  screenshots in `docs/screenshots/` taken with Playwright (dev tool, not in the repo).
- Tests: groups (leave → no read/send, last member deletes, any member adds, limits and sandbox), sandbox visibility,
  pair token single use and expiry, cleanup (25 h guest with kit, messages and avatar removed; fresh guest and seeded
  data kept; pair kept until both expire), welcome kit.

### Final review (fresh subagent) and fixes

Fixed:
- Duplicate e-mail could make e-mail login throw (Identity's e-mail index isn't unique when `RequireUniqueEmail` is off):
  check on register + filtered unique index `EmailIndex WHERE NormalizedEmail IS NOT NULL` (migration `UniqueEmail`).
- `chat.js`: catch-up and sidebar refresh also after a manual restart (when `withAutomaticReconnect` gave up);
  the sender's own sidebar now updates; readable UUID fallback; badge `aria-label` reset.
- Cross-site WebSocket hijacking: the STUDY_GUIDE wrongly credited CORS. Corrected, and the hub now rejects
  handshakes whose `Origin` isn't ours (covers sibling subdomains, which SameSite treats as the same site).
- Registration rate-limited; avatar name regex uses `\z`; 3–10 MB uploads get the friendly message (413 page text added);
  `SendAsync` returns NotFound if the member left between checks; reusing a `clientMessageId` in another conversation
  is rejected; the bot can't be added to groups.
- Dead code removed (`data-last-seen`, unused CSS, unused projections, duplicate check, stale comments); 9 MB of unused
  vendored files removed (only the minified files actually loaded are kept); `MessagesPer10Seconds` in appsettings.
- Docs: README `.html()` wording and ER diagram, shared-VPS caveats (forwarded headers, subdomains), Caddy HSTS example.
- New tests: antiforgery rejection, form send, non-member 404 on every POST, foreign Origin dropped, unique e-mail,
  `clientMessageId` reuse, bot not addable.

Not changed (accepted):
- Welcome-kit id order vs `SentAt` order is not formally guaranteed by SQL Server for a batched insert; checked on
  1 100+ rows with zero mismatches; commented in `DemoKit`.
- A guest deleted by cleanup keeps a working-looking cookie for up to 30 minutes (Identity's security-stamp interval);
  pages show nothing and return 404. Documented in STUDY_GUIDE.
- `MessageDto.SenderUserName` is kept (the spec lists the sender handle in the DTO) though the UI doesn't show it.
