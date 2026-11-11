# Publicação

Toda a configuração vem de variáveis de ambiente e da connection string, então o mesmo código roda sozinho em uma VPS
ou dividindo a VPS com outros apps. Arquivos:

| Arquivo | Para quê |
|---|---|
| `compose.prod.yaml` + `Caddyfile` | **Opção A**: VPS só deste app (SQL Server Express + app + Caddy). |
| `deploy/compose.shared.yaml` + `deploy/Caddyfile.shared-example` | **Opção B**: VPS com vários apps, um SQL Server e um Caddy compartilhados. |
| `.github/workflows/deploy.yml` | Deploy manual pelo GitHub Actions via SSH. |

Variáveis (copie `.env.example` para `.env`; o `.env` não vai para o git):

| Variável | Exemplo | Observação |
|---|---|---|
| `MSSQL_SA_PASSWORD` | `Uma_Senha_Forte_123!` | 8+ caracteres com maiúscula, minúscula, número e símbolo. |
| `LOBBY_DOMAIN` | `lobby.seudominio.com` | Caddy emite o certificado para esse nome. |
| `ALLOW_REGISTRATION` | `true` | `false` = demo só com convidados. |
| `DEMO_SIMULATE_REPLIES` | `true` | Bot e grupo de demonstração respondem. |

## Opção A: VPS só para o Lobby

### 1. Servidor novo (Ubuntu 24.04, 2 GB de RAM ou mais)

```bash
# Docker Engine + Compose v2 (script oficial)
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker

# Firewall: só SSH, HTTP e HTTPS
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp      # HTTP/3
sudo ufw enable
```

> O Docker publica portas por fora do ufw. Por isso só o Caddy publica portas (80/443) no `compose.prod.yaml`;
> o app (8080) e o SQL Server (1433) ficam só na rede interna do Docker.

### 2. DNS

Crie um registro **A** (e **AAAA**, se tiver IPv6) de `lobby.seudominio.com` apontando para o IP da VPS. Espere propagar
(`dig +short lobby.seudominio.com`) antes de subir, para o Caddy conseguir o certificado.

### 3. Subir

```bash
git clone https://github.com/<você>/lobby.git ~/lobby && cd ~/lobby
cp .env.example .env && nano .env          # MSSQL_SA_PASSWORD, LOBBY_DOMAIN
mkdir -p backups && sudo chown 10001 backups   # usuário do SQL Server no container
docker compose -f compose.prod.yaml up -d --build
docker compose -f compose.prod.yaml logs -f web   # espere "Now listening on: http://[::]:8080"
```

O app aplica as migrations e cria os dados de demonstração sozinho no primeiro start.

### 4. Conferir que o WebSocket passa pelo Caddy

Abra o site, entre como convidado, abra o DevTools → **Network** → filtro **WS**. Deve existir uma conexão
`wss://lobby.seudominio.com/hubs/chat?id=...` com status **101**. Se em vez disso aparecerem várias requisições
`/hubs/chat?id=...` repetidas (XHR), o navegador caiu para long polling, e algo no caminho está bloqueando o upgrade.
Isso foi verificado localmente com `LOBBY_DOMAIN=localhost`: o navegador usou só `wss://`.

O que o `compose.prod.yaml` já faz:

- `restart: unless-stopped` em tudo; `ASPNETCORE_ENVIRONMENT=Production`.
- SQL Server **Express** (`MSSQL_PID=Express`, grátis, até 10 GB por banco), com `MSSQL_MEMORY_LIMIT_MB=1024` e
  `mem_limit: 1536m`, senão ele ocupa quase toda a RAM da VPS.
- Volume `appdata` em `/data`: **avatares** e **chaves do Data Protection** (sem elas, todo deploy deslogaria todo mundo).
- `App__BehindProxy=true`: o app lê `X-Forwarded-For`/`X-Forwarded-Proto` do Caddy (IP real para o limite de
  convidados; cookie `Secure`; links `https://`).
- Caddy com HTTPS automático, HSTS e compressão.

### 5. Atualizar

```bash
cd ~/lobby && git pull && docker compose -f compose.prod.yaml up -d --build && docker image prune -f
```

Ou rode o workflow **Deploy** no GitHub (Actions → Deploy → Run workflow). Ele precisa dos secrets `DEPLOY_HOST`,
`DEPLOY_USER`, `DEPLOY_SSH_KEY` e `DEPLOY_KNOWN_HOSTS` (saída de `ssh-keyscan seu-host`), e opcionalmente da variável
`DEPLOY_PATH`.

**O que o usuário percebe num deploy:** o container do app reinicia. As conexões abertas caem, os navegadores mostram
"reconectando…" e voltam sozinhos em poucos segundos, buscando as mensagens que faltaram. A presença em memória é
zerada e se refaz conforme cada um reconecta. As sessões continuam válidas (chaves no volume).

### 6. Backups

Banco (todo dia às 3h, guarda 7 dias). Crie `/etc/cron.d/lobby-backup`:

```cron
0 3 * * * root cd /home/<usuário>/lobby && docker compose -f compose.prod.yaml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$(grep MSSQL_SA_PASSWORD .env | cut -d= -f2-)" -Q "BACKUP DATABASE [Lobby] TO DISK = N'/var/opt/mssql/backup/lobby-$(date +\%a).bak' WITH INIT" > /var/log/lobby-backup.log 2>&1
```

O arquivo aparece em `~/lobby/backups/` (um por dia da semana, sobrescrito na semana seguinte). Copie essa pasta para
fora da VPS (por exemplo com `rclone` para um bucket).

> Sem `COMPRESSION`: o SQL Server Express não suporta backup comprimido.

Avatares e chaves (volume `appdata`):

```bash
docker run --rm -v lobby_appdata:/data -v ~/lobby/backups:/backup alpine \
  tar czf /backup/appdata-$(date +%F).tgz -C /data .
```

(O nome real do volume aparece em `docker volume ls`; com o projeto na pasta `lobby`, é `lobby_appdata`.)

Restaurar o banco: copie o `.bak` para `backups/` e rode
`RESTORE DATABASE [Lobby] FROM DISK = N'/var/opt/mssql/backup/lobby-Mon.bak' WITH REPLACE` com o app parado.

### 7. Logs

```bash
docker compose -f compose.prod.yaml logs -f web       # app (Serilog no console)
docker compose -f compose.prod.yaml logs -f caddy     # certificados e acessos
docker compose -f compose.prod.yaml logs sqlserver
```

Para limitar o tamanho dos logs do Docker, adicione em `/etc/docker/daemon.json`:
`{"log-driver": "local", "log-opts": {"max-size": "20m", "max-file": "5"}}` e reinicie o Docker.

## Opção B: VPS compartilhada com outros apps

Um único SQL Server e um único Caddy atendem todos os apps; cada app tem **o seu banco** e **o seu subdomínio**.

1. Crie a rede compartilhada uma vez: `docker network create web`.
2. Coloque o SQL Server e o Caddy compartilhados nessa rede (um compose próprio, por exemplo `~/infra/compose.yaml`).
   O Caddy compartilhado é o único que publica 80/443.
3. No SQL Server compartilhado, crie um login só para o Lobby (melhor do que usar `sa`):

   ```sql
   CREATE LOGIN lobby_app WITH PASSWORD = 'Outra_Senha_Forte_1!';
   CREATE DATABASE Lobby;
   USE Lobby; CREATE USER lobby_app FOR LOGIN lobby_app; ALTER ROLE db_owner ADD MEMBER lobby_app;
   ```

4. No `.env` do Lobby:

   ```
   LOBBY_CONNECTION_STRING=Server=sqlserver,1433;Database=Lobby;User Id=lobby_app;Password=Outra_Senha_Forte_1!;TrustServerCertificate=True
   SHARED_NETWORK=web
   ```

5. Suba só o app: `docker compose -f deploy/compose.shared.yaml --env-file .env up -d --build`.
6. Adicione ao Caddyfile compartilhado o bloco de `deploy/Caddyfile.shared-example` (`lobby.seudominio.com` →
   `lobby-web:8080`) e recarregue: `docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile`.

Os outros apps seguem o mesmo padrão, com outro nome de serviço, outro banco e outro subdomínio.

Cuidados ao dividir a VPS:

- **Cabeçalhos encaminhados**: o app confia em `X-Forwarded-For` vindo de qualquer container da rede `web`
  (`App__BehindProxy=true` limpa a lista de proxies conhecidos). Um container de outro app nessa rede poderia falsificar
  o IP e driblar o limite de convidados por IP. Se isso importar, use uma rede só entre o Caddy e o Lobby, ou fixe a
  sub-rede do Caddy em `KnownIPNetworks` no `Program.cs`.
- **Subdomínios irmãos são o mesmo "site"** para o cookie `SameSite=Lax`. Por isso o hub também confere o cabeçalho
  `Origin` e recusa WebSockets abertos por páginas de outro subdomínio.

## Azure (resumo)

- **App Service Linux, plano B1**, publicando o container (ou o código .NET 10).
  - Ligue **Always On** (senão o app dorme e as conexões caem) e **Web sockets** (Configuração → Configurações gerais).
  - Defina `ConnectionStrings__Default`, `App__BehindProxy=true` e `App__DataPath=/home/data`. No App Service Linux,
    `/home` é armazenamento persistente compartilhado, então avatares e chaves sobrevivem a reinícios e deploys.
- **Azure SQL Database, camada Basic** (5 DTU), com o firewall liberando os serviços do Azure.
- **Por que não os planos grátis:** o App Service **F1** dorme por inatividade, tem cota de CPU por dia e não tem
  Always On, o que é ruim para conexões WebSocket longas. O Azure SQL **serverless** grátis pausa quando ocioso e leva
  dezenas de segundos para voltar; o primeiro acesso dá timeout e a demo parece quebrada.
- Para mais de uma instância seria preciso o **Azure SignalR Service** (backplane gerenciado), além de mover a presença
  para um armazenamento compartilhado. Fora do escopo deste projeto ([ADR 0003](adr/0003-in-memory-presence.md)).
