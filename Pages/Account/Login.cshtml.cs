using System.Security.Claims;
using InstagramAgendador.Web.Models;
using InstagramAgendador.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InstagramAgendador.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly InstagramAgendadorService _agendadorService;

    public LoginModel(InstagramAgendadorService agendadorService)
    {
        _agendadorService = agendadorService;
    }

    [BindProperty]
    public string Usuario { get; set; } = string.Empty;

    [BindProperty]
    public string Senha { get; set; } = string.Empty;

    [BindProperty]
    public bool LembrarSenha { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Erro { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await _agendadorService.GarantirBancoInicializadoAsync(cancellationToken);

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var usuario = await _agendadorService.AutenticarUsuarioAsync(Usuario, Senha, cancellationToken);
        if (usuario is null)
        {
            Erro = "Usuário ou senha inválidos.";
            return Page();
        }

        await RealizarSignInAsync(usuario, LembrarSenha);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Index");
    }

    private async Task RealizarSignInAsync(UsuarioAcesso usuario, bool lembrar)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Login),
            new(ClaimTypes.Role, usuario.Administrador ? "Admin" : "Usuario"),
            new("NomeExibicao", usuario.NomeExibicao),
            new("InstagramContaId", usuario.InstagramContaId?.ToString() ?? string.Empty),
            new("InstagramHandle", string.IsNullOrWhiteSpace(usuario.IdentificadorInstagramVinculado)
                ? (usuario.Administrador ? "Todos os Instagrams (Admin)" : "Perfil Vinculado")
                : usuario.IdentificadorInstagramVinculado)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = lembrar,
            AllowRefresh = true
        };

        if (lembrar)
        {
            authProps.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(90);
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            authProps);
    }
}
