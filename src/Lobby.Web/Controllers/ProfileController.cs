using Lobby.Web.Models;
using Lobby.Web.Services;
using Lobby.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lobby.Web.Controllers;

[Route("perfil")]
public class ProfileController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AvatarStorage avatars,
    ILogger<ProfileController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Edit()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return await SignOutMissingUser();
        return View(ToViewModel(user));
    }

    [HttpPost("")]
    public async Task<IActionResult> Edit(ProfileViewModel model)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return await SignOutMissingUser();
        if (!ModelState.IsValid)
        {
            var vm = ToViewModel(user);
            vm.DisplayName = model.DisplayName;
            vm.StatusMessage = model.StatusMessage;
            return View(vm);
        }

        user.DisplayName = model.DisplayName.Trim();
        user.StatusMessage = string.IsNullOrWhiteSpace(model.StatusMessage) ? null : model.StatusMessage.Trim();
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user); // the display name lives in the cookie
        TempData["Flash"] = "Perfil atualizado.";
        return RedirectToAction(nameof(Edit));
    }

    // Two limits on purpose: RequestSizeLimit stops Kestrel from reading a huge body at all,
    // and AvatarStorage checks the file's own length.
    [HttpPost("avatar")]
    [RequestSizeLimit(AvatarStorage.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarStorage.MaxRequestBytes)]
    public async Task<IActionResult> UploadAvatar(IFormFile? avatar)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return await SignOutMissingUser();

        var result = await avatars.SaveAsync(avatar);
        if (result.Error is not null)
        {
            TempData["AvatarError"] = result.Error;
            return RedirectToAction(nameof(Edit));
        }

        var oldFile = user.AvatarFileName;
        user.AvatarFileName = result.FileName;
        await userManager.UpdateAsync(user);
        avatars.Delete(oldFile); // only after the new name is saved
        logger.LogInformation("User {UserId} uploaded avatar {FileName}", user.Id, result.FileName);
        await signInManager.RefreshSignInAsync(user);
        TempData["Flash"] = "Foto atualizada.";
        return RedirectToAction(nameof(Edit));
    }

    [HttpPost("avatar/remover")]
    public async Task<IActionResult> RemoveAvatar()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return await SignOutMissingUser();
        var oldFile = user.AvatarFileName;
        user.AvatarFileName = null;
        await userManager.UpdateAsync(user);
        avatars.Delete(oldFile);
        await signInManager.RefreshSignInAsync(user);
        TempData["Flash"] = "Foto removida.";
        return RedirectToAction(nameof(Edit));
    }

    [HttpGet("senha")]
    public IActionResult ChangePassword()
    {
        if (User.IsGuest()) return NotFound();
        return View(new ChangePasswordViewModel());
    }

    [HttpPost("senha")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (User.IsGuest()) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var user = await userManager.GetUserAsync(User);
        if (user is null) return await SignOutMissingUser();

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Errors.Any(e => e.Code == "PasswordMismatch")
                ? "A senha atual está incorreta."
                : "A nova senha precisa ter pelo menos 8 caracteres, com letras e números.");
            return View(model);
        }
        await signInManager.RefreshSignInAsync(user);
        TempData["Flash"] = "Senha alterada.";
        return RedirectToAction(nameof(Edit));
    }

    private static ProfileViewModel ToViewModel(ApplicationUser user) => new()
    {
        UserId = user.Id,
        UserName = user.UserName!,
        DisplayName = user.DisplayName,
        StatusMessage = user.StatusMessage,
        AvatarFileName = user.AvatarFileName,
        IsGuest = user.IsGuest,
    };

    // A guest cookie can outlive the guest row (cleanup runs hourly).
    private async Task<IActionResult> SignOutMissingUser()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/conta/entrar");
    }
}
