using System.Security.Claims;
using InstagramAgendador.Web.Models;
using InstagramAgendador.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InstagramAgendador.Web.Pages;

[AllowAnonymous]
public sealed class ConectarInstagramModel : PageModel
{
    private readonly InstagramAgendadorService _agendadorService;
    private readonly InstagramGraphApiService _graphApiService;

    public ConectarInstagramModel(
        InstagramAgendadorService agendadorService,
        InstagramGraphApiService graphApiService)
    {
        _agendadorService = agendadorService;
        _graphApiService = graphApiService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Uid { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sig { get; set; }

    public UsuarioAcesso? UsuarioAlvo { get; private set; }
    public bool MetaOAuthConfigurado { get; private set; }
    public string? MensagemSucesso { get; private set; }
    public string? MensagemErro { get; private set; }
    public IReadOnlyList<ContaInstagramDescoberta> ContasDescobertas { get; private set; } = [];
    public string RedirectUriCallback => $"{ObterBaseUrlPublica()}/ConectarInstagram?handler=CallbackMeta";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await CarregarContextoAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetIniciarOAuthAsync(
        int? uid,
        string? sig,
        CancellationToken cancellationToken)
    {
        Uid = uid;
        Sig = sig;
        await CarregarContextoAsync(cancellationToken);

        var (appId, appSecret) = await _agendadorService.ObterCredenciaisAppMetaAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            MensagemErro = "O Administrador ainda não salvou o App ID e App Secret da Meta na tela de Configurações.";
            return Page();
        }

        var idDestino = UsuarioAlvo?.Id ?? 0;
        var sigDestino = idDestino > 0
            ? InstagramAgendadorService.GerarAssinaturaConvite(idDestino)
            : "admin";

        var state = $"{idDestino}:{sigDestino}";
        var scopes = "instagram_basic,instagram_content_publish,pages_show_list,pages_read_engagement,business_management";

        var urlFacebookOAuth =
            $"https://www.facebook.com/v21.0/dialog/oauth?client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(RedirectUriCallback)}&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(scopes)}&response_type=code";

        return Redirect(urlFacebookOAuth);
    }

    public async Task<IActionResult> OnGetCallbackMetaAsync(
        string? code,
        string? state,
        string? error,
        string? error_description,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(state))
        {
            var partes = state.Split(':', 2);
            if (partes.Length == 2 && int.TryParse(partes[0], out var parsedUid) && parsedUid > 0)
            {
                Uid = parsedUid;
                Sig = partes[1];
            }
        }

        await CarregarContextoAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            MensagemErro = !string.IsNullOrWhiteSpace(error_description)
                ? $"Conexão cancelada ou recusada no Facebook: {error_description}"
                : "A autorização no Facebook não foi concluída.";
            return Page();
        }

        try
        {
            var (appId, appSecret) = await _agendadorService.ObterCredenciaisAppMetaAsync(cancellationToken);
            var tokenCurto = await _graphApiService.TrocarCodigoOAuthPorTokenAsync(
                code,
                RedirectUriCallback,
                appId,
                appSecret,
                cancellationToken);

            var descobertas = await _graphApiService.DescobrirContasPorTokenAsync(
                tokenCurto,
                appId,
                appSecret,
                cancellationToken);

            if (descobertas.Count == 0)
            {
                MensagemErro =
                    "Login no Facebook realizado, mas nenhuma conta do Instagram Comercial vinculada a uma Página do Facebook foi encontrada. Verifique se o seu Instagram está como Conta Empresarial e conectado a uma Página do Facebook.";
                return Page();
            }

            if (descobertas.Count == 1)
            {
                var unica = descobertas[0];
                var salva = await SalvarContaDescobertaNoBancoAsync(unica, cancellationToken);
                MensagemSucesso =
                    $"Pronto! O Instagram {salva.IdentificadorInstagram} ({salva.NomeCadastro}) foi conectado automaticamente com Token de 60 dias!";
                return Page();
            }

            ContasDescobertas = descobertas;
            return Page();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MensagemErro = ex.GetBaseException().Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSelecionarContaAsync(
        [FromForm] int? uid,
        [FromForm] string? sig,
        [FromForm] string accountIdMeta,
        [FromForm] string identificadorInstagram,
        [FromForm] string nomeExibicao,
        [FromForm] string accessTokenFinal,
        CancellationToken cancellationToken)
    {
        Uid = uid;
        Sig = sig;
        await CarregarContextoAsync(cancellationToken);

        try
        {
            var descoberta = new ContaInstagramDescoberta(
                AccountIdMeta: accountIdMeta,
                IdentificadorInstagram: identificadorInstagram,
                NomeExibicao: nomeExibicao,
                AccessTokenFinal: accessTokenFinal,
                TokenConvertido60Dias: true);

            var salva = await SalvarContaDescobertaNoBancoAsync(descoberta, cancellationToken);
            MensagemSucesso =
                $"Pronto! O Instagram {salva.IdentificadorInstagram} ({salva.NomeCadastro}) foi conectado e configurado com sucesso!";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MensagemErro = ex.GetBaseException().Message;
        }

        return Page();
    }

    private async Task<InstagramContaCadastro> SalvarContaDescobertaNoBancoAsync(
        ContaInstagramDescoberta descoberta,
        CancellationToken cancellationToken)
    {
        var todasContas = await _agendadorService.ListarContasAsync(cancellationToken);
        var existente = todasContas.FirstOrDefault(c =>
            c.AccountIdMeta == descoberta.AccountIdMeta ||
            c.IdentificadorInstagram.Equals(descoberta.IdentificadorInstagram, StringComparison.OrdinalIgnoreCase));

        var idUsuarioDestino = UsuarioAlvo?.Id ?? existente?.UsuarioAcessoId;

        return await _agendadorService.SalvarContaInstagramAsync(
            contaId: existente?.Id,
            nomeCadastro: existente?.NomeCadastro ?? descoberta.NomeExibicao,
            identificadorInstagram: descoberta.IdentificadorInstagram,
            accountIdMeta: descoberta.AccountIdMeta,
            accessTokenMeta: descoberta.AccessTokenFinal,
            graphApiBaseUrl: "https://graph.facebook.com/v21.0",
            baseUrlPublica: ObterBaseUrlPublica(),
            horariosFeedTexto: null,
            horariosStoryTexto: null,
            publicacaoAutomaticaAtiva: true,
            definirComoPadrao: todasContas.Count == 0,
            usuarioAcessoId: idUsuarioDestino,
            cancellationToken: cancellationToken);
    }

    private async Task CarregarContextoAsync(CancellationToken cancellationToken)
    {
        var (appId, appSecret) = await _agendadorService.ObterCredenciaisAppMetaAsync(cancellationToken);
        MetaOAuthConfigurado = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(appSecret);

        var usuarios = await _agendadorService.ListarUsuariosAsync(cancellationToken);

        if (Uid is > 0 && InstagramAgendadorService.ValidarAssinaturaConvite(Uid.Value, Sig))
        {
            UsuarioAlvo = usuarios.FirstOrDefault(u => u.Id == Uid.Value);
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            var idLogadoStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idLogadoStr, out var idLogado))
            {
                var alvoId = (User.IsInRole("Admin") && Uid is > 0) ? Uid.Value : idLogado;
                UsuarioAlvo = usuarios.FirstOrDefault(u => u.Id == alvoId);
            }
        }
    }

    private string ObterBaseUrlPublica()
    {
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        var proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
            ?? (host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ? Request.Scheme : "https");
        return $"{proto}://{host}";
    }
}
