using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Lobby.Web.Data;
using Lobby.Web.Hubs;
using Lobby.Web.Models;
using Lobby.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logging) => logging
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false); // don't advertise the server

var dataPath = Path.GetFullPath(builder.Configuration["App:DataPath"] ?? "data", builder.Environment.ContentRootPath);

// ---- Data ----
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Default"),
    sql => sql.EnableRetryOnFailure()));
builder.Services.AddSingleton(TimeProvider.System);

// ---- Identity (cookie auth, no roles, no scaffolded UI) ----
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789._-";
        options.User.RequireUniqueEmail = false; // guests and seeded users have no e-mail
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<LobbyClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/conta/entrar";
    options.AccessDeniedPath = "/erro/403";
    // Lax (the default, set explicitly because the hub relies on it): the cookie is not
    // sent on cross-site POSTs or cross-site WebSocket handshakes started by other sites.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    // AJAX calls and the hub get a plain 401 instead of a redirect to the login page.
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/hubs") || context.Request.Headers.XRequestedWith == "XMLHttpRequest")
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else
            context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Keys persisted to the data volume: without this, every redeploy would log everyone out.
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

// Every endpoint requires a signed-in user unless it says [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
// Razor still HTML-encodes everything; this only stops it from escaping "ã" as "&#xE3;".
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// ---- Rate limiting for guest creation (per IP) ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var guestsPerHour = builder.Configuration.GetValue("RateLimits:GuestsPerHourPerIp", 10);
    options.AddPolicy("guest", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = guestsPerHour, Window = TimeSpan.FromHours(1) }));
});

// ---- App services ----
builder.Services.AddSingleton(new AvatarStorage(Path.Combine(dataPath, "avatars")));
builder.Services.AddScoped<GuestService>();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddScoped<PeopleService>();
builder.Services.AddScoped<DirectConversationService>();
builder.Services.AddScoped<ConversationQueries>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<DemoKit>();
builder.Services.AddScoped<GuestCleanup>();
builder.Services.AddHostedService<GuestCleanupService>();

// Demo responder (demo only): the bot and the demo group answer guests.
var simulateReplies = builder.Configuration.GetValue("Demo:SimulateReplies", false);
builder.Services.AddSingleton(new DemoReplyQueue(simulateReplies));
if (simulateReplies) builder.Services.AddHostedService<DemoResponder>();

// ---- SignalR ----
// Detailed errors would send exception messages to browsers, so only in Development.
builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddSingleton<IChatBroadcaster, ChatBroadcaster>();
// About 10 messages per 10 seconds per user. One singleton, so the count is shared by the
// hub, the form fallback and every tab of the same user.
builder.Services.AddSingleton(PartitionedRateLimiter.Create<string, string>(userId =>
    RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = builder.Configuration.GetValue("RateLimits:MessagesPer10Seconds", 10),
        Window = TimeSpan.FromSeconds(10),
    })));
// No AddCors on purpose: other origins can't read our HTTP responses (pages, negotiate, history).
// CORS doesn't cover WebSockets, though: there the SameSite=Lax cookie and the Origin check in
// ChatHub.OnConnectedAsync do the job (see STUDY_GUIDE §7).

var app = builder.Build();

// Behind Caddy, the client IP and https scheme arrive in X-Forwarded-* headers.
// Only enabled when configured, because trusting these headers without a proxy lets clients spoof their IP.
if (app.Configuration.GetValue<bool>("App:BehindProxy"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear(); // the proxy is another container with an unknown IP;
    forwarded.KnownProxies.Clear();    // the app port is never published, so only Caddy can reach us.
    app.UseForwardedHeaders(forwarded);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/erro");
app.UseStatusCodePagesWithReExecute("/erro/{0}");

app.UseSerilogRequestLogging();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets().AllowAnonymous();
app.MapControllers().WithStaticAssets();
app.MapHub<ChatHub>("/hubs/chat");

await DbInitializer.InitializeAsync(app.Services, app.Logger);

app.Run();

public partial class Program; // lets WebApplicationFactory<Program> find this assembly in tests
