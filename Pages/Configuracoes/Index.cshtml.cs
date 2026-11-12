using InstagramAgendador.Web.Models;
using InstagramAgendador.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InstagramAgendador.Web.Pages.Configuracoes;

public sealed class IndexModel : PageModel
{
    private readonly InstagramAgendadorService _agendadorService;

    public IndexModel(InstagramAgendadorService agendadorService)
    {
        _agendadorService = agendadorService;
    }

    public IReadOnlyList<InstagramContaCadastro> ContasInstagram { get; private set; } = [];
    public IReadOnlyList<UsuarioAcesso> UsuariosAcesso { get; private set; } = [];
    public string BaseUrlDetectada { get; private set; } = string.Empty;

    private bool EhAdmin => User.IsInRole("Admin");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] =
                "Acesso restrito: somente o Administrador pode incluir perfis do Instagram para os usuários e alterar configurações.";
            return RedirectToPage("/Index");
        }

        BaseUrlDetectada = ObterBaseUrlPublica();
        ContasInstagram = await _agendadorService.ListarContasAsync(cancellationToken);
        UsuariosAcesso = await _agendadorService.ListarUsuariosAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSalvarContaInstagramAsync(
        [FromForm] int? contaId,
        [FromForm] int? usuarioAcessoId,
        [FromForm] string nomeCadastro,
        [FromForm] string identificadorInstagram,
        [FromForm] string accountIdMeta,
        [FromForm] string? accessTokenMeta,
        [FromForm] string? graphApiBaseUrl,
        [FromForm] string? baseUrlPublica,
        [FromForm] string? horariosFeed,
        [FromForm] string? horariosStory,
        [FromForm] bool publicacaoAutomaticaAtiva,
        [FromForm] bool definirComoPadrao,
        [FromForm] string? novoUsuarioNome,
        [FromForm] string? novoUsuarioLogin,
        [FromForm] string? novoUsuarioSenha,
        [FromForm] bool novoUsuarioAdmin,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            var urlBase = string.IsNullOrWhiteSpace(baseUrlPublica)
                ? ObterBaseUrlPublica()
                : baseUrlPublica;

            int? idUsuarioDestino = usuarioAcessoId is > 0 ? usuarioAcessoId : null;
            string msgUsuario = string.Empty;

            // Se o Admin optou por criar um novo usuário junto com o perfil do Instagram
            if (!idUsuarioDestino.HasValue &&
                !string.IsNullOrWhiteSpace(novoUsuarioLogin) &&
                !string.IsNullOrWhiteSpace(novoUsuarioSenha))
            {
                var nomeUsr = string.IsNullOrWhiteSpace(novoUsuarioNome)
                    ? nomeCadastro
                    : novoUsuarioNome;

                var novoUsr = await _agendadorService.SalvarUsuarioAcessoAsync(
                    usuarioId: null,
                    nomeExibicao: nomeUsr,
                    login: novoUsuarioLogin,
                    senha: novoUsuarioSenha,
                    instagramContaId: null,
                    administrador: novoUsuarioAdmin,
                    cancellationToken: cancellationToken);

                idUsuarioDestino = novoUsr.Id;
                msgUsuario = $" Usuário '{novoUsr.Login}' criado e vinculado a este perfil.";
            }

            var conta = await _agendadorService.SalvarContaInstagramAsync(
                contaId,
                nomeCadastro,
                identificadorInstagram,
                accountIdMeta,
                accessTokenMeta,
                graphApiBaseUrl,
                urlBase,
                horariosFeed,
                horariosStory,
                publicacaoAutomaticaAtiva,
                definirComoPadrao,
                idUsuarioDestino,
                cancellationToken);

            TempData["Mensagem"] =
                $"Perfil {conta.IdentificadorInstagram} ({conta.NomeCadastro}) e sua respectiva configuração salvos com sucesso!{msgUsuario}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestarConexaoAsync(
        [FromForm] int contaId,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            var resultado = await _agendadorService.TestarESalvarStatusContaAsync(contaId, cancellationToken);
            if (!resultado.Sucesso)
            {
                TempData["TipoMensagem"] = "erro";
            }

            TempData["Mensagem"] = resultado.Mensagem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAlternarContaAtivaAsync(
        [FromForm] int contaId,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            await _agendadorService.AlternarContaAtivaAsync(contaId, cancellationToken);
            TempData["Mensagem"] = "Status do perfil do Instagram atualizado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExcluirContaAsync(
        [FromForm] int contaId,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            await _agendadorService.ExcluirContaAsync(contaId, cancellationToken);
            TempData["Mensagem"] = "Perfil de Instagram removido.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSalvarUsuarioAsync(
        [FromForm] int? usuarioId,
        [FromForm] string nomeExibicao,
        [FromForm] string login,
        [FromForm] string? senha,
        [FromForm] int? instagramContaId,
        [FromForm] bool administrador,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            var usuario = await _agendadorService.SalvarUsuarioAcessoAsync(
                usuarioId,
                nomeExibicao,
                login,
                senha,
                instagramContaId,
                administrador: administrador,
                cancellationToken: cancellationToken);

            var destinoIg = string.IsNullOrWhiteSpace(usuario.IdentificadorInstagramVinculado)
                ? (usuario.Administrador ? "Todos os Instagrams (Admin)" : "Sem perfil vinculado ainda")
                : usuario.IdentificadorInstagramVinculado;

            TempData["Mensagem"] =
                $"Usuário '{usuario.Login}' ({usuario.NomeExibicao}) salvo com sucesso! Perfil vinculado: {destinoIg}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExcluirUsuarioAsync(
        [FromForm] int usuarioId,
        CancellationToken cancellationToken)
    {
        if (!EhAdmin)
        {
            return Forbid();
        }

        try
        {
            await _agendadorService.ExcluirUsuarioAsync(usuarioId, cancellationToken);
            TempData["Mensagem"] = "Usuário removido com sucesso.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.Message;
        }

        return RedirectToPage();
    }

    public string FormatarHorarios(string json, string padrao) =>
        string.Join(", ", InstagramAgendadorService.DeserializarHorarios(json, [padrao]));

    private string ObterBaseUrlPublica()
    {
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        var proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
            ?? (host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ? Request.Scheme : "https");
        return $"{proto}://{host}";
    }
}
