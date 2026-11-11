using Lobby.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MsSql;

namespace Lobby.Tests.Infrastructure;

// One SQL Server container for the whole test run, migrated and seeded once.
// Tests never clean up: each one creates its own users and conversations, so they don't collide.
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private LobbyFactory? _factory;

    public string ConnectionString { get; private set; } = "";

    // Started on first use, so pure database tests don't pay for the web host.
    public LobbyFactory Factory => _factory ??= new LobbyFactory(ConnectionString);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "LobbyTests" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await DbInitializer.SeedAsync(db, TimeProvider.System); // like app startup: seeded people + bot exist
    }

    public AppDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(interceptors)
            .Options);

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
