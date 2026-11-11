using System.ComponentModel.DataAnnotations;
using Lobby.Web.Models;

namespace Lobby.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Informe seu usuário ou e-mail.")]
    [Display(Name = "Usuário ou e-mail")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Informe sua senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = "";

    [Display(Name = "Manter conectado")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Escolha um nome de usuário.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Use de 3 a 20 caracteres.")]
    [RegularExpression("^[a-z0-9._-]+$", ErrorMessage = "Use apenas letras minúsculas, números, ponto, hífen ou sublinhado.")]
    [Display(Name = "Nome de usuário")]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "Informe como quer ser chamado.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "Use até 40 caracteres.")]
    [Display(Name = "Nome de exibição")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Escolha uma senha.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "A senha precisa ter pelo menos 8 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirme a senha")]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfileViewModel
{
    [Required(ErrorMessage = "Informe seu nome de exibição.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "Use até 40 caracteres.")]
    [Display(Name = "Nome de exibição")]
    public string DisplayName { get; set; } = "";

    [StringLength(ApplicationUser.StatusMessageMaxLength, ErrorMessage = "Use até 80 caracteres.")]
    [Display(Name = "Recado")]
    public string? StatusMessage { get; set; }

    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? AvatarFileName { get; set; }
    public bool IsGuest { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha atual")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Escolha a nova senha.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "A senha precisa ter pelo menos 8 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nova senha")]
    public string NewPassword { get; set; } = "";

    [Compare(nameof(NewPassword), ErrorMessage = "As senhas não conferem.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirme a nova senha")]
    public string ConfirmPassword { get; set; } = "";
}
