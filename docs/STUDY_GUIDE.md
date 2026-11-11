# Guia de estudo do Lobby

Objetivo: conseguir explicar qualquer linha do projeto em uma entrevista. Leia na ordem abaixo, com o código aberto ao lado.

## 1. Ordem de leitura

1. **`src/Lobby.Web/Program.cs`**: tudo o que é registrado e a ordem do pipeline (autenticação → autorização →
   rate limiting → controllers → hub). Aqui moram as decisões globais: política de fallback (tudo exige login),
   antiforgery global, cookie `SameSite=Lax`, ausência de CORS, SignalR, limite de mensagens.
2. **`Models/`**: `ApplicationUser`, `Conversation`, `ConversationMember`, `Message`. Depois **`Data/AppDbContext.cs`**:
   índices, check constraint, cascata vs. restrict. Compare com a migration `Data/Migrations/*_Conversations.cs`.
3. **`Services/MembershipService.cs`** e **`Services/PeopleService.cs`**: as duas regras de acesso (quem lê uma
   conversa; quem vê quem).
4. **`Services/DirectConversationService.cs`** (seção 3 deste guia).
5. **`Services/MessageService.cs`** (seção 4) e **`Hubs/ChatHub.cs`**, **`Hubs/ChatBroadcaster.cs`**.
6. **`Services/ConversationQueries.cs`**: barra lateral em uma consulta e paginação por chave.
7. **`Controllers/ChatController.cs`** e **`Views/Chat/`**: como a página é montada e o fallback sem JavaScript.
8. **`wwwroot/js/chat.js`**: o cliente.
9. **`Services/PresenceTracker.cs`**, **`Services/AvatarStorage.cs`**, **`Services/GuestService.cs`**,
   **`Services/GuestCleanup.cs`**, **`Services/DemoKit.cs`**, **`Services/DemoResponder.cs`**.
10. **`tests/Lobby.Tests/`**: comece por `ChatHubTests.cs` e `MessageServiceTests.cs`.

## 2. A vida de uma mensagem: do Enter até a outra aba

1. **Teclado** (`chat.js`): Enter sem Shift dispara o `submit` do formulário. Se a conexão SignalR não estiver
   conectada, o `submit` segue normal (POST de formulário, ver passo 9).
2. **Bolha otimista**: o JS gera `clientMessageId` (`crypto.randomUUID()`), clona o `<template id="message-template">`,
   preenche com `.text(...)` e mostra "enviando…".
3. **`connection.invoke('SendMessage', conversationId, body, clientMessageId)`**: vai pelo WebSocket já aberto.
   O SignalR serializa em JSON, e o servidor chama `ChatHub.SendMessage`.
4. **Hub**: pega o usuário de `Context.UserIdentifier` (vem do cookie, ver seção 6) e chama `MessageService.SendAsync`.
5. **`SendAsync`**: valida, checa se é membro, aplica o limite, checa duplicata, salva (seção 4).
6. **Após o commit**: lê os membros atuais e chama `Clients.Users(ids).SendAsync("messageReceived", dto)`.
   O SignalR acha **todas** as conexões abertas de cada id (cada aba é uma conexão) e envia.
7. **Resposta ao remetente**: o hub retorna o `MessageDto`; a promise do `invoke` resolve e o JS confirma a bolha
   (tira "enviando…", grava `data-id`).
8. **Na outra aba/pessoa**: o handler `messageReceived` recebe o DTO. Se o `clientMessageId` já está na tela
   (minha outra aba, ou a resposta chegou depois do broadcast), só confirma. Se é da conversa aberta, adiciona e rola
   para baixo se o usuário já estava perto do fim; senão mostra "novas mensagens ↓". Se é de outra conversa, atualiza
   a barra lateral (prévia, contador, sobe para o topo) ou recarrega a lista se a conversa é nova.
9. **Sem JavaScript**: `POST /c/{id}/messages` → `ChatController.Send` → o **mesmo** `SendAsync` → redirect (PRG).
   Os outros membros recebem em tempo real do mesmo jeito, porque o broadcast está dentro do `SendAsync`.

## 3. `DirectConversationService.GetOrCreateAsync`, linha a linha

```csharp
public async Task<int?> GetOrCreateAsync(string myId, string otherUserId)
```
Retorna o id da conversa direta, ou `null` quando não é permitido. `int?` evita exceções para casos esperados.

```csharp
if (myId == otherUserId) return null;
if (!await people.CanSeeAsync(myId, otherUserId)) return null;
```
Sem conversa consigo mesmo. `CanSeeAsync` usa `PeopleService.VisibleTo`, a regra do sandbox: um convidado só vê o
seu par e as pessoas de demonstração; ninguém vê convidados. Isso impede, por exemplo, que um usuário real comece
uma conversa com um visitante digitando o id na URL.

```csharp
var key = Conversation.MakeDirectKey(myId, otherUserId);
```
`"{menorId}|{maiorId}"` com `string.CompareOrdinal`. Ordinal compara bytes, sem regras de idioma, então o mesmo par
gera a mesma chave em qualquer máquina e em qualquer ordem dos argumentos (`(ana, bia)` e `(bia, ana)`).

```csharp
var existingId = await FindByKeyAsync(key);
if (existingId is not null) return existingId;
```
Caminho comum: a conversa já existe. `FindByKeyAsync` busca só o `Id` (projeção), pelo índice único em `DirectKey`.

```csharp
var conversation = new Conversation { Type = Direct, DirectKey = key, CreatedById = myId, ..., Members = [eu, outro] };
db.Conversations.Add(conversation);
await db.SaveChangesAsync();
return conversation.Id;
```
Conversa e os dois membros entram no **mesmo** `SaveChanges`. O EF Core envolve um `SaveChanges` em uma transação,
então nunca existe conversa sem membros. Depois do save, o EF preenche `conversation.Id` com o valor gerado (identity).

```csharp
catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
```
**A corrida.** Ana e Bia clicam "Enviar mensagem" uma para a outra no mesmo instante. As duas requisições passam
pelo `FindByKeyAsync` (ainda não existe nada) e as duas tentam inserir. O índice único filtrado
(`DirectKey WHERE DirectKey IS NOT NULL`) deixa só o primeiro INSERT passar; o segundo recebe o erro 2627
(violação de unique constraint) ou 2601 (chave duplicada em índice único). O filtro `when` captura **só** esse erro;
qualquer outro problema continua sendo exceção.

```csharp
db.Entry(conversation).State = EntityState.Detached;
foreach (var member in conversation.Members) db.Entry(member).State = EntityState.Detached;
```
O EF ainda acha que precisa inserir essas entidades. Desanexamos para que um `SaveChanges` posterior no mesmo
`DbContext` não tente inseri-las de novo. (Não usei `ChangeTracker.Clear()`, porque isso descartaria também outras
entidades que a mesma requisição esteja rastreando.)

```csharp
return await FindByKeyAsync(key) ?? throw ...;
```
Agora a conversa que "ganhou" existe: devolvemos o id dela. As duas pessoas acabam na mesma conversa.
Testes: `Race_where_the_other_insert_wins_returns_the_winning_conversation` (determinístico, com um interceptor que
insere a conversa concorrente logo antes do nosso save) e `Two_contexts_racing_for_the_same_pair_end_with_exactly_one_conversation`.

## 4. Membro, `ChatHub.SendMessage` e `MessageService.SendAsync`, linha a linha

**A regra de autorização** é uma só: `MembershipService.IsMemberAsync(conversationId, userId)`, um
`AnyAsync` em `ConversationMembers`. Usada pela página (`/c/{id}`), pelo histórico (`/c/{id}/messages`), pelo POST de
formulário e pelo hub (dentro do `SendAsync`). Quem não é membro recebe **404** (MVC) ou
`HubException("Conversa não encontrada")` (hub), a mesma resposta de uma conversa que não existe. Assim não dá para
descobrir quais ids existem.

```csharp
public async Task<MessageDto> SendMessage(int conversationId, string body, Guid clientMessageId)
{
    var result = await messages.SendAsync(UserId, conversationId, body, clientMessageId);
    return result.Error switch { None => result.Message!, InvalidBody => throw new HubException("..."), ... };
}
```
O hub é fino: só traduz o resultado. `HubException` é a única exceção cuja mensagem chega ao cliente. Qualquer outra
vira "An unexpected error occurred…" sem detalhes, porque `EnableDetailedErrors` fica desligado fora de Development.

```csharp
body = body?.Trim();
if (string.IsNullOrEmpty(body) || body.Length > Message.MaxBodyLength || clientMessageId == Guid.Empty)
    return SendResult.Fail(SendError.InvalidBody);
```
Valida **depois** do trim: "   " é vazio. 2000 é o limite do texto já aparado. O cliente também valida, mas o
servidor nunca confia no cliente.

```csharp
if (!await membership.IsMemberAsync(conversationId, senderId)) return SendResult.Fail(SendError.NotFound);
```
A regra de autorização. Vem antes de qualquer escrita.

```csharp
using (var lease = rateLimiter.AttemptAcquire(senderId)) { if (!lease.IsAcquired) return ...RateLimited; }
```
`PartitionedRateLimiter` singleton: uma janela fixa de 10 mensagens por 10 s **por usuário** (a partição é o id).
Fica aqui e não no middleware de rate limiting porque o middleware enxerga requisições HTTP; as chamadas ao hub
viajam todas dentro de uma única conexão WebSocket. E aqui vale também para o POST de formulário.

```csharp
var existing = await FindAsync(senderId, clientMessageId);
if (existing is not null) return new SendResult(SendError.None, existing);
```
**Idempotência.** Se essa mensagem já foi salva (o cliente reenviou porque a conexão caiu antes da resposta),
devolve a existente sem inserir e **sem transmitir de novo**.

```csharp
var member = await db.ConversationMembers.Include(m => m.Conversation).SingleAsync(...);
db.Messages.Add(message);
member.Conversation.LastMessageAt = now;
member.LastReadAt = now;
await db.SaveChangesAsync();
```
**Persistir primeiro.** Mensagem, `LastMessageAt` (ordem da barra lateral) e o meu `LastReadAt` (minha própria
mensagem nunca conta como não lida) em uma única transação. `SentAt` vem do `TimeProvider` do servidor, nunca do
relógio do cliente.

```csharp
catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
    return new SendResult(SendError.None, await FindAsync(senderId, clientMessageId));
```
Duas cópias da mesma mensagem chegaram juntas e passaram pela checagem de duplicata ao mesmo tempo. O índice único
`(SenderId, ClientMessageId)` deixa uma só; a outra devolve a que ganhou. Quem salvou é quem transmite.

```csharp
var dto = await db.Messages...Select(MessageDto.FromMessage).SingleAsync();
var memberIds = await membership.GetMemberIdsAsync(conversationId);
try { await broadcaster.MessageCreatedAsync(memberIds, dto); }
catch (Exception ex) { logger.LogWarning(...); }
```
**Só depois do commit.** Se o `SaveChanges` lançou exceção, nunca chegamos aqui: nada é anunciado sem estar no banco
(teste `When_SaveChanges_fails_nothing_is_broadcast`). Os membros são lidos **agora**, então quem entrou há um
segundo recebe e quem saiu não recebe. Se o broadcast falhar, a mensagem continua salva; o banco é a fonte da verdade e
os clientes recuperam pelo histórico ou pelo catch-up após reconectar. Por isso o erro é registrado e engolido.

```csharp
demoReplies.Enqueue(dto);
return new SendResult(SendError.None, dto);
```
Avisa o respondedor da demo (só com `Demo:SimulateReplies`) e devolve o DTO, que o hub retorna para quem enviou.

## 5. Conexão caiu, e agora?

- O navegador perde a rede. Se a queda foi "limpa", o servidor recebe o fechamento na hora; se foi abrupta, só percebe
  quando o tempo limite expira (~30 s). Aí `OnDisconnectedAsync` roda, e a presença muda se era a última conexão.
- No cliente, `withAutomaticReconnect()` tenta de novo em 0, 2, 10 e 30 s e mostra "reconectando…". Se desistir,
  nosso `onclose` tenta a cada 10 s.
- Uma mensagem enviada durante a queda: o `invoke` falha, a bolha fica vermelha com "tentar novamente".
  O botão reenvia com o **mesmo** `clientMessageId`. Se o servidor tinha salvado (a resposta é que se perdeu),
  o `SendAsync` devolve a existente; se não tinha, salva agora. Nunca duplica.
- Ao reconectar (pelo `onreconnected`, ou pelo nosso `start()` depois que o `withAutomaticReconnect` desistiu):
  `GET /c/{id}/messages?after={último id confirmado}` traz o que chegou enquanto estava fora
  (até 200; se houver mais, a página recarrega), e a barra lateral é recarregada. Bolhas pendentes cujo
  `clientMessageId` aparece na resposta são marcadas como enviadas.

- Um convidado apagado pela limpeza ainda pode ter o cookie no navegador: ele vê listas vazias e recebe 404 até o
  `SecurityStampValidator` do Identity revalidar o cookie (a cada 30 min por padrão), quando é deslogado.

## 6. Como o cookie chega ao hub

1. O login (`SignInManager.SignInAsync`) grava o cookie `.AspNetCore.Identity.Application`, com as claims:
   `NameIdentifier` (id), `is_guest`, nome de exibição e avatar (via `LobbyClaimsPrincipalFactory`).
2. O cliente SignalR faz `POST /hubs/chat/negotiate` na mesma origem; o navegador envia o cookie junto.
   O middleware de autenticação lê o cookie, e o `[Authorize]` do hub deixa passar. Sem cookie: 401
   (configuramos `OnRedirectToLogin` para não redirecionar `/hubs`).
3. A resposta do negotiate traz um `connectionToken` e os transportes disponíveis (WebSockets, Server-Sent Events,
   Long Polling). O cliente abre `wss://.../hubs/chat?id=<token>`; o cookie vai de novo no handshake.
4. No hub, `Context.UserIdentifier` = claim `NameIdentifier` (o `IUserIdProvider` padrão). É esse id que
   `Clients.Users(...)` usa para achar as conexões.

## 7. Por que antiforgery não se aplica a hubs, e o que protege no lugar

Antiforgery protege **formulários e AJAX** contra CSRF: outro site não consegue ler o token da nossa página, então não
consegue montar um POST válido. Aqui isso está ligado globalmente (`AutoValidateAntiforgeryTokenAttribute`), e o jQuery
envia o token no cabeçalho `RequestVerificationToken` com um único `$.ajaxSetup`.

Um WebSocket não tem esse mecanismo, e **CORS também não se aplica a WebSockets** (o navegador abre a conexão para
qualquer origem; e o cliente SignalR pode pular o negotiate com `skipNegotiation`). O risco é o
**Cross-Site WebSocket Hijacking**: uma página maliciosa abre um WebSocket para o nosso servidor e o navegador manda o
cookie da vítima junto. O que impede:

- **Cookie `SameSite=Lax`**: o navegador não envia o cookie em requisições iniciadas por **outro site** que não sejam
  navegação de primeiro nível. Um handshake de WebSocket aberto por `evil.com` vai sem cookie, e o `[Authorize]` do
  hub recusa (401).
- **Checagem de `Origin` no hub** (`ChatHub.OnConnectedAsync`): "site" no SameSite é o domínio registrável, então
  `outro-app.seudominio.com` conta como o mesmo site e o cookie iria junto. Mas o navegador sempre envia o cabeçalho
  `Origin` no handshake, e o hub derruba a conexão se ele não for exatamente a nossa origem.
- **Sem CORS** (não chamamos `AddCors`): isso protege as respostas HTTP (páginas, histórico, negotiate), que outra
  origem não consegue ler. Não é o que protege o WebSocket.

## 8. Roteiro de demo de 5 minutos (duas janelas)

1. (30 s) Abra o site. "É um chat em tempo real em ASP.NET Core MVC com SignalR. Vou entrar como visitante."
   Clique **Entrar como convidado**.
2. (45 s) Mostre a barra lateral: conversa com o bot (1 não lida) e o grupo (3 não lidas). Abra o bot, mande "oi":
   a resposta chega em 1–3 s sem recarregar. "O bot responde pelo mesmo `MessageService`, o mesmo caminho de uma pessoa."
3. (60 s) Copie o **link de par** e abra numa janela anônima. Clique "Entrar e conversar". Na primeira janela a conversa
   com o novo visitante aparece sozinha na lateral. Mostre a bolinha verde nas duas.
4. (60 s) Converse entre as janelas. Mostre "enviando…" virando enviada, Shift+Enter, o contador de caracteres.
   Feche a janela anônima: na outra, a bolinha fica cinza e aparece "visto agora há pouco".
5. (45 s) Abra o "Clube do livro (demo)" e role para cima: o histórico carrega de 30 em 30 sem a tela pular.
6. (60 s) Mostre o código: `MessageService.SendAsync` (persistir primeiro, transmitir depois) e `ChatBroadcaster`
   (`Clients.Users`). Termine com o teste `User_added_to_a_group_while_connected_receives_the_next_message_without_reconnecting`.

## 9. Perguntas prováveis de entrevista

**WebSockets ou long polling?** O SignalR negocia: tenta WebSockets (uma conexão TCP full-duplex, menor latência), depois
Server-Sent Events, depois long polling (requisições HTTP que o servidor segura até ter algo). Em produção, atrás do Caddy,
verifiquei que o navegador usa `wss://`. Nos testes de integração uso long polling, porque o servidor de testes em
memória não tem WebSockets de verdade.

**O que tem numa requisição de negotiate?** Um `POST /hubs/chat/negotiate?negotiateVersion=1` com o cookie. A resposta
traz `connectionId`, `connectionToken` e `availableTransports` (cada transporte com seus formatos, texto ou binário).
O cliente escolhe o melhor e se conecta com `?id=<connectionToken>`.

**O que acontece com duas abas?** São duas conexões. O `PresenceTracker` conta 2; a pessoa só fica offline quando a
segunda fecha. `Clients.Users` entrega nas duas. A aba que enviou ignora a cópia pelo `clientMessageId`.

**Por que não grupos do SignalR por conversa?** Grupos guardam conexões e ficam desatualizados: quem entra não recebe e
quem sai continua recebendo até reconectar. Com `Clients.Users`, os membros são lidos do banco a cada envio
([ADR 0002](adr/0002-clients-users-instead-of-groups.md)).

**E se o banco falhar ao salvar?** O `SaveChanges` lança exceção, o método sai antes do broadcast, o hub devolve um erro
genérico e a bolha fica "tentar novamente". Ninguém vê uma mensagem que não existe. Com o mesmo `clientMessageId`, o
reenvio é seguro.

**Como impedir que alguém de fora leia uma conversa?** Uma regra só: existe linha em `ConversationMembers`? Ela é
checada na página, no histórico, no POST e no hub. Quem não é membro recebe 404, igual a uma conversa inexistente.
Há testes HTTP e SignalR para isso.

**Como você escalaria isso?** Várias instâncias + backplane do SignalR (Redis) ou Azure SignalR Service para os
`Clients.Users` chegarem a conexões em outros servidores; presença em um armazenamento compartilhado (Redis com TTL);
limite de mensagens distribuído; avatares em blob storage; sticky sessions se não usar só WebSockets.

**Por que checar os magic bytes?** A extensão e o Content-Type vêm do cliente e podem mentir. Um HTML renomeado para
`.png` com `image/png` passaria. Os primeiros bytes mostram o que o arquivo é de verdade. E o `nosniff` impede o
navegador de "adivinhar" que é HTML.

**Por que não `.html()`?** `.html()` interpreta texto como HTML: uma mensagem `<img src=x onerror=...>` executaria
script na tela de todos (XSS armazenado). Com `.text()` o navegador mostra os caracteres literalmente. Conteúdo vindo
do servidor como HTML (histórico, barra lateral) já foi codificado pelo Razor.

**Por que paginação por chave (`Id < before`) e não `Skip/Take`?** `OFFSET` fica mais lento quanto mais fundo se vai
e "pula" ou repete mensagens quando chegam novas no meio. Com `WHERE Id < @before ORDER BY Id DESC` o índice
`(ConversationId, Id)` vai direto ao ponto, e cada página é estável.

**Por que buscar 31 para mostrar 30?** Para saber se existe página anterior sem um `COUNT` extra.

**Como a barra lateral evita N+1?** É uma única consulta projetada: a prévia, o contador de não lidas e a outra pessoa
são subconsultas correlacionadas que o EF transforma em um único SQL.

**Por que `Restrict` nas chaves para usuário?** O SQL Server recusa dois caminhos de cascata até a mesma linha (usuário →
mensagem e usuário → conversa → mensagem). E apagar usuários é raro e perigoso; preferi uma ordem explícita e testada
na limpeza de convidados.

**Como o servidor sabe que é um convidado sem consultar o banco?** A claim `is_guest` vai no cookie, adicionada pelo
`LobbyClaimsPrincipalFactory`. Já o id do par é lido do banco, porque pode ser definido depois que o cookie foi emitido.

**Por que o link de par só é gasto no POST?** Aplicativos de mensagem e e-mail fazem GET para gerar pré-visualização. Se o
GET gastasse o token de uso único, o link chegaria inválido.

**Por que `TimeProvider`?** Para testar o que depende de tempo sem esperar: os testes de expiração do link (30 min) e da
limpeza (24 h) usam `FakeTimeProvider` e avançam o relógio.

**O que é `EnableRetryOnFailure` e por que a limpeza usa `CreateExecutionStrategy`?** O EF repete comandos que falham
por erros transitórios do SQL Server. Com uma transação aberta manualmente, ele não pode repetir só um comando no meio,
então a transação inteira vai dentro de `strategy.ExecuteAsync(...)`, e o bloco todo é repetido.
