# ADR 0001: SignalR inside the MVC app

**Status:** accepted

## Context
The Odin "Messaging App" brief is a REST API plus a separate front end, and leaves real time out. The target job uses
ASP.NET Core MVC, Razor, jQuery and SQL Server. Chat without live delivery is the least convincing part of a chat demo.

## Options
1. **SignalR hub in the same ASP.NET Core MVC app**, server-rendered Razor pages, a small jQuery client.
2. A separate real-time service (another process or a message broker) next to the MVC app.
3. An SPA (React/Angular) talking to a Web API plus SignalR.

## Decision
Option 1. One process hosts the pages, the JSON/HTML endpoints and the hub at `/hubs/chat`.

- The hub uses the same Identity cookie as the pages, so authentication is shared, with no tokens and no CORS.
- The hub and the no-JS form POST call the same `MessageService.SendAsync`, so there is one code path for saving.
- Razor still renders everything that has to be safe by default (history pages, sidebar); JavaScript only adds live behaviour.

## Consequences
- \+ Small: two projects, no npm, no bundler, one Docker image. Every piece is explainable in an interview.
- \+ SignalR picks the best transport (WebSockets, then Server-Sent Events, then long polling) and reconnects.
- \- One instance only, until a backplane is added (see ADR 0003).
- \- Mixing server-rendered HTML with live DOM updates means the message markup exists twice: in `_Messages.cshtml`
  and in the `<template>` cloned by `chat.js`. Both are short and kept side by side.
