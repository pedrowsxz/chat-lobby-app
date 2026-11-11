using Bogus;
using Lobby.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lobby.Web.Data;

public static class DbInitializer
{
    public const string BotUserName = "assistente";

    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // In Docker the web container can start before SQL Server accepts logins.
        // EnableRetryOnFailure covers transient errors, but not "server not up yet", so retry here.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                break;
            }
            catch (SqlException ex) when (attempt < 20)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        await SeedAsync(db, time);
    }

    // Deterministic and idempotent: runs only on an empty database, with a fixed random seed.
    public static async Task SeedAsync(AppDbContext db, TimeProvider time)
    {
        if (await db.Users.AnyAsync()) return;

        Randomizer.Seed = new Random(4321);
        var faker = new Faker("pt_BR");
        var now = time.GetUtcNow();

        for (var i = 0; i < 15; i++)
        {
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            var handle = $"{Ascii(first)}.{Ascii(last)}".ToLowerInvariant();
            if (db.Users.Local.Any(u => u.UserName == handle)) handle += i;
            var user = NewSeededUser(handle, $"{first} {last}", DemoContent.StatusMessages[i], now.AddDays(-30));
            user.Id = faker.Random.Guid().ToString();
            db.Users.Add(user);
        }

        var bot = NewSeededUser(BotUserName, "Assistente Lobby", "Bot de demonstração 🤖", now.AddDays(-30));
        bot.IsDemoBot = true;
        db.Users.Add(bot);

        await db.SaveChangesAsync();
    }

    // No PasswordHash at all: nobody can sign in as a seeded user, so seeded data never changes.
    private static ApplicationUser NewSeededUser(string handle, string displayName, string status, DateTimeOffset createdAt) => new()
    {
        UserName = handle,
        NormalizedUserName = handle.ToUpperInvariant(),
        DisplayName = displayName,
        StatusMessage = status,
        IsSeeded = true,
        CreatedAt = createdAt,
        SecurityStamp = Guid.NewGuid().ToString(),
    };

    private static string Ascii(string text) =>
        new(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsAsciiLetter).ToArray());
}
