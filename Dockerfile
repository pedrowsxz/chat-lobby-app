# Build stage: full SDK, restore first so the package layer is cached between builds.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Lobby.Web/Lobby.Web.csproj src/Lobby.Web/
RUN dotnet restore src/Lobby.Web/Lobby.Web.csproj
COPY src/Lobby.Web/ src/Lobby.Web/
RUN dotnet publish src/Lobby.Web/Lobby.Web.csproj -c Release -o /app --no-restore

# Runtime stage: ASP.NET runtime only, running as the image's built-in non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# /data is mounted as a volume (avatars + Data Protection keys). Create it owned by the
# app user so a fresh named volume inherits that ownership and stays writable.
RUN mkdir -p /data/avatars /data/keys && chown -R $APP_UID /data
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    App__DataPath=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lobby.Web.dll"]
