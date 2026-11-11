namespace Lobby.Web.ViewModels;

// Size: "sm" (sidebar, bubbles), "md" (lists), "lg" (profile).
// Online = null means "don't draw a presence dot" (the bot, or the user themself).
public record AvatarViewModel(string UserId, string DisplayName, string? AvatarFileName, string Size = "md", bool? Online = null);
