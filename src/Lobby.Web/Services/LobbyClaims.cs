using System.Security.Claims;
using Lobby.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Lobby.Web.Services;

public static class LobbyClaims
{
    public const string IsGuestType = "is_guest";
    public const string DisplayNameType = "display_name";
    public const string AvatarType = "avatar";

    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user id claim.");

    public static bool IsGuest(this ClaimsPrincipal user) => user.HasClaim(IsGuestType, "true");

    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(DisplayNameType) ?? user.Identity?.Name ?? "";

    public static string? GetAvatar(this ClaimsPrincipal user) => user.FindFirstValue(AvatarType);
}

// Puts is_guest, display name and avatar into the auth cookie. The hub can then
// tell a guest apart without a database query, and the layout can draw the
// navbar without one. After a profile change we call RefreshSignInAsync.
public class LobbyClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(LobbyClaims.IsGuestType, user.IsGuest ? "true" : "false"));
        identity.AddClaim(new Claim(LobbyClaims.DisplayNameType, user.DisplayName));
        if (user.AvatarFileName is not null)
            identity.AddClaim(new Claim(LobbyClaims.AvatarType, user.AvatarFileName));
        return identity;
    }
}
