using Lobby.Web.Models;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lobby.Web.Controllers;

// A small hand-written controller instead of the scaffolded Identity UI:
// only the four flows this app needs, all in one readable file.
[Route("conta")]
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    GuestService guests,
    DemoKit demoKit,
    MessageService messages,
    TimeProvider time,
    IConfiguration config) : Controller
{
    private bool RegistrationAllowed => config.GetValue("App:AllowRegistration", true);

    [AllowAnonymous]
    [HttpGet("entrar")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect("/");
        ViewBag.AllowRegistration = RegistrationAllowed;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("entrar")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewBag.AllowRegistration = RegistrationAllowed;
        if (!ModelState.IsValid) return View(model);

        var user = model.Login.Contains('@')
            ? await userManager.FindByEmailAsync(model.Login)
            : await userManager.FindByNameAsync(model.Login);

        // Guests and seeded users have no password, so they can never sign in here.
        var result = user is null || user.IsGuest
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");

        ModelState.AddModelError("", result.IsLockedOut
            ? "Muitas tentativas. Tente de novo em alguns minutos."
            : "Usuário ou senha incorretos.");
        return View(model);
    }

    [AllowAnonymous]
    [HttpGet("criar")]
    public IActionResult Register()
    {
        if (!RegistrationAllowed) return NotFound();
        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost("criar")]
    [EnableRateLimiting("guest")] // each account gets a welcome kit and is never cleaned up
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!RegistrationAllowed) return NotFound();
        if (model.UserName.StartsWith("convidado", StringComparison.Ordinal))
            ModelState.AddModelError(nameof(model.UserName), "Esse nome é reservado para visitantes.");
        // Identity's RequireUniqueEmail is off because guests have no e-mail, so we check here
        // (and a filtered unique index in AppDbContext backs it up).
        if (ModelState.IsValid && await userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "Esse e-mail já está cadastrado.");
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", TranslateIdentityError(error));
            return View(model);
        }

        await demoKit.CreateForAsync(user);
        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect("/");
    }

    [AllowAnonymous]
    [HttpPost("convidado")]
    [EnableRateLimiting("guest")]
    public async Task<IActionResult> Guest()
    {
        var guest = await guests.CreateGuestAsync();
        await demoKit.CreateForAsync(guest);
        await signInManager.SignInAsync(guest, isPersistent: false);
        return LocalRedirect("/");
    }

    // The pair link. GET only shows a page with a button; the token is spent on POST.
    // Link previews (WhatsApp, Slack, e-mail scanners) fetch with GET, and would otherwise
    // burn the single-use token before the person clicks it.
    [AllowAnonymous]
    [HttpGet("/par/{token}")]
    public async Task<IActionResult> Pair(string token)
    {
        ViewBag.Token = token;
        ViewBag.AlreadySignedIn = User.Identity?.IsAuthenticated == true;
        var inviter = await guests.FindInviterAsync(token);
        return View("Pair", inviter?.DisplayName);
    }

    [AllowAnonymous]
    [HttpPost("/par/{token}")]
    [EnableRateLimiting("guest")]
    public async Task<IActionResult> AcceptPair(string token)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect("/");

        var pair = await guests.AcceptPairAsync(token);
        if (pair is null)
        {
            ViewBag.Token = token;
            ViewBag.AlreadySignedIn = false;
            return View("Pair", null);
        }

        await demoKit.CreateForAsync(pair.Guest);
        await signInManager.SignInAsync(pair.Guest, isPersistent: false);
        // A first message from the new guest. It goes through the normal broadcast, so the
        // DM appears in the inviter's sidebar right away, in their other window.
        await messages.SendAsync(pair.Guest.Id, pair.ConversationId, Data.DemoContent.PairHello, Guid.NewGuid());
        return LocalRedirect($"/c/{pair.ConversationId}");
    }

    [HttpPost("sair")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/conta/entrar");
    }

    private static string TranslateIdentityError(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" => "Esse nome de usuário já está em uso.",
        "InvalidUserName" => "Nome de usuário inválido.",
        "InvalidEmail" => "E-mail inválido.",
        _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => "A senha precisa ter pelo menos 8 caracteres, com letras e números.",
        _ => "Não foi possível criar a conta.",
    };
}
