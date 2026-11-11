using Microsoft.AspNetCore.Identity;

namespace Lobby.Web.Models;

public class ApplicationUser : IdentityUser
{
    public const int DisplayNameMaxLength = 40;
    public const int StatusMessageMaxLength = 80;

    public string DisplayName { get; set; } = "";

    // Free profile text ("lendo Dom Casmurro"). Online/offline is NOT stored:
    // it lives in memory in PresenceTracker, because it changes on every connect.
    public string? StatusMessage { get; set; }

    // "{guid}.{ext}" inside App:DataPath/avatars. Null = initials avatar.
    public string? AvatarFileName { get; set; }

    public bool IsGuest { get; set; }

    // Set on both guests of a pair (see the pair link). Not a foreign key: the two rows point
    // at each other and are always deleted together by guest cleanup.
    public string? GuestPairId { get; set; }

    // Single-use invite for the pair link (30 min). Cleared the moment someone uses it.
    public string? PairToken { get; set; }
    public DateTimeOffset? PairTokenExpiresAt { get; set; }

    // Seeded demo people and the bot. Guests may see only these, themselves and their pair.
    public bool IsSeeded { get; set; }
    public bool IsDemoBot { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Written when the user's last connection closes, so the UI can say "visto há 5 min".
    public DateTimeOffset? LastSeenAt { get; set; }
}
