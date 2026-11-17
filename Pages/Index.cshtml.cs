using System.Globalization;
using System.Security.Claims;
using InstagramAgendador.Web.Data;
using InstagramAgendador.Web.Models;
using InstagramAgendador.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace InstagramAgendador.Web.Pages;

public sealed record ItemCalendarioMes(
    string UrlMiniatura,
    string IdentificadorInstagram,
    string Titulo,
    string HorarioTexto,
    string TipoMidia,
    string StatusClasse,
    string AncoraAlvo);

public sealed class IndexModel : PageModel
{
    private readonly IDbContextFactory<AgendadorDbContext> _dbFactory;
    private readonly InstagramAgendadorService _agendadorService;

    public IndexModel(
        IDbContextFactory<AgendadorDbContext> dbFactory,
        InstagramAgendadorService agendadorService)
    {
        _dbFactory = dbFactory;
        _agendadorService = agendadorService;
    }

    [BindProperty(SupportsGet = true)]
    public int? ContaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Mes { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Ano { get; set; }

    public bool EhAdmin => User.IsInRole("Admin");
    public IReadOnlyList<InstagramContaCadastro> ContasDisponiveis { get; private set; } = [];
    public InstagramContaCadastro? ContaSelecionada { get; private set; }
    public IReadOnlyList<DateTime> ProximosSlotsConta { get; private set; } = [];
    public IReadOnlyList<InstagramPostAgendado> PostsAgendados { get; private set; } = [];
    public IReadOnlyList<InstagramStoryRecorrente> StoriesRecorrentes { get; private set; } = [];

    public int MesCalendario { get; private set; }
    public int AnoCalendario { get; private set; }
    public IReadOnlyDictionary<int, List<ItemCalendarioMes>> CalendarioPorDia { get; private set; } =
        new Dictionary<int, List<ItemCalendarioMes>>();

    public DateTime AgoraSaoPaulo => FusoHorarioService.AgoraLocal.DateTime;
    public int TotalPostsNaFila { get; private set; }
    public int TotalStoriesAtivos { get; private set; }
    public int TotalPublicados { get; private set; }
    public int TotalFalhas { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await CarregarPainelAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCriarPostAgendadoAsync(
        [FromForm] int instagramContaId,
        [FromForm] string titulo,
        [FromForm] string legenda,
        [FromForm] string tipoPublicacao,
        [FromForm] string modoHorario,
        [FromForm] string? horarioEspecificoLocal,
        [FromForm] int[]? diasSemanaPost,
        [FromForm] string? horarioDiaSemanaPost,
        [FromForm] string? urlImagemExterna,
        [FromForm] IFormFile? arquivoImagem,
        [FromForm] bool publicarImediatamente,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[]? bytesImagem = null;
            string? contentType = null;

            if (arquivoImagem is { Length: > 0 })
            {
                await using var ms = new MemoryStream();
                await arquivoImagem.CopyToAsync(ms, cancellationToken);
                bytesImagem = ms.ToArray();
                contentType = arquivoImagem.ContentType;
            }

            if (string.Equals(modoHorario, "dias_semana", StringComparison.OrdinalIgnoreCase))
            {
                if (diasSemanaPost is null || diasSemanaPost.Length == 0)
                {
                    throw new ArgumentException("Selecione pelo menos um dia da semana para o agendamento.");
                }

                var postSemanal = await _agendadorService.CriarPostAgendadoAsync(
                    instagramContaId,
                    titulo,
                    legenda,
                    tipoPublicacao,
                    bytesImagem,
                    contentType,
                    urlImagemExterna,
                    horarioEscolhidoLocal: null,
                    ObterBaseUrlPublica(),
                    recorrenteSemanal: true,
                    diasSemana: diasSemanaPost,
                    horarioFixoSemanal: horarioDiaSemanaPost,
                    cancellationToken: cancellationToken);

                var diasTexto = InstagramAgendadorService.FormatarDiasSemanaResumo(postSemanal.DiasSemanaJson);
                TempData["Mensagem"] =
                    $"Publicação semanal '{postSemanal.Titulo}' programada para repetir toda semana em [{diasTexto}] às {postSemanal.HorarioFixoSemanal} para {postSemanal.IdentificadorInstagram}! Próximo disparo: {postSemanal.HorarioProgramadoLocal:dd/MM/yyyy 'às' HH:mm}.";

                return RedirectToPage(new { ContaId = instagramContaId });
            }

            DateTime? horarioEscolhido = null;
            if (string.Equals(modoHorario, "manual", StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParseExact(
                        (horarioEspecificoLocal ?? string.Empty).Trim(),
                        ["yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "dd/MM/yyyy HH:mm"],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed))
                {
                    throw new ArgumentException("Informe uma data e horário válidos para o agendamento.");
                }

                horarioEscolhido = parsed;
            }

            var post = await _agendadorService.CriarPostAgendadoAsync(
                instagramContaId,
                titulo,
                legenda,
                tipoPublicacao,
                bytesImagem,
                contentType,
                urlImagemExterna,
                horarioEscolhido,
                ObterBaseUrlPublica(),
                cancellationToken: cancellationToken);

            if (publicarImediatamente)
            {
                var (sucesso, msgPub) = await _agendadorService.PublicarPostAgoraAsync(
                    post.Id,
                    ObterBaseUrlPublica(),
                    cancellationToken);

                if (!sucesso)
                {
                    TempData["TipoMensagem"] = "erro";
                }

                TempData["Mensagem"] = msgPub;
            }
            else
            {
                TempData["Mensagem"] =
                    $"Publicação '{post.Titulo}' agendada para {post.IdentificadorInstagram} em {post.HorarioProgramadoLocal:dd/MM/yyyy 'às' HH:mm} (America/Sao_Paulo).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId = instagramContaId });
    }

    public async Task<IActionResult> OnPostEditarPostAgendadoAsync(
        [FromForm] int postId,
        [FromForm] int instagramContaId,
        [FromForm] string titulo,
        [FromForm] string legenda,
        [FromForm] string horarioProgramadoLocal,
        [FromForm] string tipoPublicacao,
        [FromForm] string? urlImagemExterna,
        [FromForm] IFormFile? novaImagem,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!DateTime.TryParseExact(
                    (horarioProgramadoLocal ?? string.Empty).Trim(),
                    ["yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "dd/MM/yyyy HH:mm"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dataHora))
            {
                throw new ArgumentException("Informe uma data e hora válidas.");
            }

            byte[]? novosBytes = null;
            string? novoContentType = null;
            if (novaImagem is { Length: > 0 })
            {
                await using var ms = new MemoryStream();
                await novaImagem.CopyToAsync(ms, cancellationToken);
                novosBytes = ms.ToArray();
                novoContentType = novaImagem.ContentType;
            }

            await _agendadorService.EditarPostAgendadoAsync(
                postId,
                instagramContaId,
                titulo,
                legenda,
                dataHora,
                tipoPublicacao,
                novosBytes,
                novoContentType,
                urlImagemExterna,
                ObterBaseUrlPublica(),
                cancellationToken);

            TempData["Mensagem"] = $"Agendamento #{postId} atualizado com sucesso.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostPublicarPostAgoraAsync(
        [FromForm] int postId,
        CancellationToken cancellationToken)
    {
        try
        {
            var (sucesso, mensagem) = await _agendadorService.PublicarPostAgoraAsync(
                postId,
                ObterBaseUrlPublica(),
                cancellationToken);

            if (!sucesso)
            {
                TempData["TipoMensagem"] = "erro";
            }

            TempData["Mensagem"] = mensagem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostCancelarPostAsync(
        [FromForm] int postId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _agendadorService.CancelarPostAgendadoAsync(postId, cancellationToken);
            TempData["Mensagem"] = $"Agendamento #{postId} cancelado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostExcluirPostAsync(
        [FromForm] int postId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _agendadorService.ExcluirPostAgendadoAsync(postId, cancellationToken);
            TempData["Mensagem"] = $"Agendamento #{postId} excluído.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostCriarStoryRecorrenteAsync(
        [FromForm] int instagramContaId,
        [FromForm] string titulo,
        [FromForm] string modoAgendamento,
        [FromForm] string? horariosDiarios,
        [FromForm] int[]? diasSemana,
        [FromForm] string? dataHoraUnicaLocal,
        [FromForm] IFormFile? imagemStory,
        CancellationToken cancellationToken)
    {
        try
        {
            if (imagemStory is null || imagemStory.Length == 0)
            {
                throw new ArgumentException("Selecione uma imagem ou arte vertical (9:16) do seu celular/computador.");
            }

            await using var ms = new MemoryStream();
            await imagemStory.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            DateTime? dataHoraUnica = null;
            if (string.Equals(modoAgendamento, ModoStoryRecorrente.UnicaVez, StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParseExact(
                        (dataHoraUnicaLocal ?? string.Empty).Trim(),
                        ["yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "dd/MM/yyyy HH:mm"],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed))
                {
                    throw new ArgumentException("Informe uma data e hora válidas para o disparo único.");
                }

                dataHoraUnica = parsed;
            }

            var story = await _agendadorService.CriarStoryRecorrenteAsync(
                instagramContaId,
                titulo,
                bytes,
                imagemStory.ContentType,
                modoAgendamento,
                horariosDiarios,
                dataHoraUnica,
                ObterBaseUrlPublica(),
                diasSemana,
                cancellationToken);

            TempData["Mensagem"] =
                $"Story '{story.Titulo}' programado para {story.IdentificadorInstagram} ({InstagramAgendadorService.FormatarDiasSemanaResumo(story.DiasSemanaJson)})! Próximo disparo: {story.ProximoDisparoLocal:dd/MM/yyyy 'às' HH:mm}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId = instagramContaId });
    }

    public async Task<IActionResult> OnPostEditarStoryRecorrenteAsync(
        [FromForm] int storyId,
        [FromForm] int instagramContaId,
        [FromForm] string titulo,
        [FromForm] string modoAgendamento,
        [FromForm] string? horariosDiarios,
        [FromForm] int[]? diasSemana,
        [FromForm] string? dataHoraUnicaLocal,
        [FromForm] IFormFile? novaImagemStory,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[]? novosBytes = null;
            string? novoContentType = null;
            if (novaImagemStory is { Length: > 0 })
            {
                await using var ms = new MemoryStream();
                await novaImagemStory.CopyToAsync(ms, cancellationToken);
                novosBytes = ms.ToArray();
                novoContentType = novaImagemStory.ContentType;
            }

            DateTime? dataHoraUnica = null;
            if (string.Equals(modoAgendamento, ModoStoryRecorrente.UnicaVez, StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParseExact(
                        (dataHoraUnicaLocal ?? string.Empty).Trim(),
                        ["yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "dd/MM/yyyy HH:mm"],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed))
                {
                    throw new ArgumentException("Informe uma data e hora válidas.");
                }

                dataHoraUnica = parsed;
            }

            await _agendadorService.EditarStoryRecorrenteAsync(
                storyId,
                instagramContaId,
                titulo,
                modoAgendamento,
                horariosDiarios,
                dataHoraUnica,
                novosBytes,
                novoContentType,
                ObterBaseUrlPublica(),
                diasSemana,
                cancellationToken);

            TempData["Mensagem"] = $"Story programado #{storyId} atualizado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostPublicarStoryAgoraAsync(
        [FromForm] int storyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var (sucesso, mensagem) = await _agendadorService.PublicarStoryRecorrenteAgoraAsync(
                storyId,
                ObterBaseUrlPublica(),
                cancellationToken);

            if (!sucesso)
            {
                TempData["TipoMensagem"] = "erro";
            }

            TempData["Mensagem"] = mensagem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostAlternarStoryAtivoAsync(
        [FromForm] int storyId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _agendadorService.AlternarStoryAtivoAsync(storyId, cancellationToken);
            TempData["Mensagem"] = $"Status do Story #{storyId} alterado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public async Task<IActionResult> OnPostExcluirStoryAsync(
        [FromForm] int storyId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _agendadorService.ExcluirStoryRecorrenteAsync(storyId, cancellationToken);
            TempData["Mensagem"] = $"Story programado #{storyId} excluído.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["TipoMensagem"] = "erro";
            TempData["Mensagem"] = ex.GetBaseException().Message;
        }

        return RedirectToPage(new { ContaId });
    }

    public string ObterHorariosStoryTexto(InstagramStoryRecorrente story) =>
        string.Join(", ", InstagramAgendadorService.DeserializarHorarios(story.HorariosDiariosJson, ["10:00", "19:00"]));

    private string ObterBaseUrlPublica()
    {
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        var proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
            ?? (host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ? Request.Scheme : "https");
        return $"{proto}://{host}";
    }

    private async Task CarregarPainelAsync(CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _ = int.TryParse(userIdStr, out var usuarioId);

        ContasDisponiveis = await _agendadorService.ListarContasParaUsuarioAsync(
            usuarioId,
            EhAdmin,
            cancellationToken);

        if (ContaId.HasValue && ContasDisponiveis.Any(c => c.Id == ContaId.Value))
        {
            ContaSelecionada = ContasDisponiveis.First(c => c.Id == ContaId.Value);
        }
        else
        {
            var claimContaIdStr = User.FindFirst("InstagramContaId")?.Value;
            if (int.TryParse(claimContaIdStr, out var contaPadraoUsuario) &&
                ContasDisponiveis.Any(c => c.Id == contaPadraoUsuario))
            {
                ContaSelecionada = ContasDisponiveis.First(c => c.Id == contaPadraoUsuario);
            }
            else
            {
                ContaSelecionada = ContasDisponiveis.FirstOrDefault(c => c.Padrao)
                    ?? ContasDisponiveis.FirstOrDefault();
            }

            ContaId = ContaSelecionada?.Id;
        }

        if (ContaSelecionada is not null)
        {
            ProximosSlotsConta = await _agendadorService.CalcularProximosHorariosFeedAsync(
                ContaSelecionada.Id,
                10,
                cancellationToken);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var queryPosts = db.PostsAgendados.AsNoTracking();
        var queryStories = db.StoriesRecorrentes.AsNoTracking();

        if (ContaSelecionada is not null)
        {
            queryPosts = queryPosts.Where(p => p.InstagramContaId == ContaSelecionada.Id);
            queryStories = queryStories.Where(s => s.InstagramContaId == ContaSelecionada.Id);
        }

        var postsRaw = await queryPosts
            .OrderByDescending(p => p.Id)
            .Take(100)
            .Select(p => new InstagramPostAgendado
            {
                Id = p.Id,
                InstagramContaId = p.InstagramContaId,
                IdentificadorInstagram = p.IdentificadorInstagram,
                Titulo = p.Titulo,
                Legenda = p.Legenda,
                TipoPublicacao = p.TipoPublicacao,
                ContentTypeImagem = p.ContentTypeImagem,
                UrlImagemExterna = p.UrlImagemExterna,
                BaseUrlPublica = p.BaseUrlPublica,
                HorarioProgramadoLocal = p.HorarioProgramadoLocal,
                HorarioProgramadoUtc = p.HorarioProgramadoUtc,
                RecorrenteSemanal = p.RecorrenteSemanal,
                DiasSemanaJson = p.DiasSemanaJson,
                HorarioFixoSemanal = p.HorarioFixoSemanal,
                Status = p.Status,
                CreationIdMeta = p.CreationIdMeta,
                MediaIdMeta = p.MediaIdMeta,
                Tentativas = p.Tentativas,
                UltimoErro = p.UltimoErro,
                CriadoEmUtc = p.CriadoEmUtc,
                PublicadoEmUtc = p.PublicadoEmUtc
            })
            .ToListAsync(cancellationToken);

        PostsAgendados = postsRaw
            .OrderBy(p => p.Status == StatusPostInstagram.Agendado ? 0 : 1)
            .ThenBy(p => p.Status == StatusPostInstagram.Agendado ? p.HorarioProgramadoUtc : DateTimeOffset.MinValue)
            .ThenByDescending(p => p.CriadoEmUtc)
            .ToList();

        var storiesRaw = await queryStories
            .Select(s => new InstagramStoryRecorrente
            {
                Id = s.Id,
                InstagramContaId = s.InstagramContaId,
                IdentificadorInstagram = s.IdentificadorInstagram,
                Titulo = s.Titulo,
                ContentTypeImagem = s.ContentTypeImagem,
                BaseUrlPublica = s.BaseUrlPublica,
                ModoAgendamento = s.ModoAgendamento,
                HorariosDiariosJson = s.HorariosDiariosJson,
                DiasSemanaJson = s.DiasSemanaJson,
                DataHoraUnicaLocal = s.DataHoraUnicaLocal,
                ProximoDisparoLocal = s.ProximoDisparoLocal,
                ProximoDisparoUtc = s.ProximoDisparoUtc,
                Ativo = s.Ativo,
                Status = s.Status,
                TotalPublicacoes = s.TotalPublicacoes,
                TentativasNoHorarioAtual = s.TentativasNoHorarioAtual,
                UltimoMediaIdMeta = s.UltimoMediaIdMeta,
                UltimoErro = s.UltimoErro,
                UltimaPublicacaoEmUtc = s.UltimaPublicacaoEmUtc,
                CriadoEmUtc = s.CriadoEmUtc
            })
            .ToListAsync(cancellationToken);

        StoriesRecorrentes = storiesRaw
            .OrderByDescending(s => s.Ativo)
            .ThenBy(s => s.ProximoDisparoUtc)
            .ToList();

        TotalPostsNaFila = PostsAgendados.Count(p =>
            p.Status == StatusPostInstagram.Agendado ||
            p.Status == StatusPostInstagram.Publicando);
        TotalStoriesAtivos = StoriesRecorrentes.Count(s => s.Ativo);
        TotalPublicados = PostsAgendados.Count(p => p.Status == StatusPostInstagram.Publicado)
            + StoriesRecorrentes.Sum(s => s.TotalPublicacoes);
        TotalFalhas = PostsAgendados.Count(p => p.Status == StatusPostInstagram.Falha)
            + StoriesRecorrentes.Count(s => !string.IsNullOrWhiteSpace(s.UltimoErro));

        // Monta o Calendário do Mês respeitando estritamente o Instagram Ativo selecionado (ContaSelecionada)
        var agora = AgoraSaoPaulo;
        AnoCalendario = Ano is >= 2024 and <= 2035 ? Ano.Value : agora.Year;
        MesCalendario = Mes is >= 1 and <= 12 ? Mes.Value : agora.Month;

        var postsCalendario = PostsAgendados
            .Where(p => p.Status != StatusPostInstagram.Cancelado)
            .ToList();

        var storiesCalendario = StoriesRecorrentes
            .Where(s => s.Ativo)
            .ToList();

        var mapaDias = new Dictionary<int, List<ItemCalendarioMes>>();
        var totalDiasNoMes = DateTime.DaysInMonth(AnoCalendario, MesCalendario);

        void AdicionarNoDia(int dia, ItemCalendarioMes item)
        {
            if (dia < 1 || dia > totalDiasNoMes) return;
            if (!mapaDias.TryGetValue(dia, out var lista))
            {
                lista = [];
                mapaDias[dia] = lista;
            }
            lista.Add(item);
        }

        foreach (var p in postsCalendario)
        {
            var imgUrl = !string.IsNullOrWhiteSpace(p.UrlImagemExterna)
                ? p.UrlImagemExterna
                : $"/media/post/{p.Id}.jpg?v={p.CriadoEmUtc.UtcTicks}";

            var statusCls = p.Status switch
            {
                StatusPostInstagram.Publicado => "publicado",
                StatusPostInstagram.Falha => "falha",
                _ => "agendado"
            };

            if (p.HorarioProgramadoLocal.Year == AnoCalendario &&
                p.HorarioProgramadoLocal.Month == MesCalendario)
            {
                AdicionarNoDia(
                    p.HorarioProgramadoLocal.Day,
                    new ItemCalendarioMes(
                        UrlMiniatura: imgUrl,
                        IdentificadorInstagram: p.IdentificadorInstagram,
                        Titulo: p.Titulo,
                        HorarioTexto: p.HorarioProgramadoLocal.ToString("HH:mm"),
                        TipoMidia: p.TipoPublicacao,
                        StatusClasse: statusCls,
                        AncoraAlvo: $"#post-card-{p.Id}"));
            }

            // Se for recorrente semanal ativo, projeta nos demais dias da semana escolhidos neste mês
            if (p.RecorrenteSemanal && p.Status == StatusPostInstagram.Agendado)
            {
                var diasSem = InstagramAgendadorService.DeserializarDiasSemana(p.DiasSemanaJson);
                var horaFixa = p.HorarioFixoSemanal ?? p.HorarioProgramadoLocal.ToString("HH:mm");
                for (var d = 1; d <= totalDiasNoMes; d++)
                {
                    var dataDia = new DateTime(AnoCalendario, MesCalendario, d);
                    if (dataDia.Date < agora.Date) continue;
                    if (dataDia.Date == p.HorarioProgramadoLocal.Date) continue;
                    if (diasSem.Contains((int)dataDia.DayOfWeek))
                    {
                        AdicionarNoDia(
                            d,
                            new ItemCalendarioMes(
                                UrlMiniatura: imgUrl,
                                IdentificadorInstagram: p.IdentificadorInstagram,
                                Titulo: $"{p.Titulo} (Semanal)",
                                HorarioTexto: horaFixa,
                                TipoMidia: p.TipoPublicacao,
                                StatusClasse: "agendado",
                                AncoraAlvo: $"#post-card-{p.Id}"));
                    }
                }
            }
        }

        foreach (var s in storiesCalendario)
        {
            var imgStoryUrl = $"/media/story/{s.Id}.jpg?v={s.CriadoEmUtc.UtcTicks}";
            if (s.ModoAgendamento == ModoStoryRecorrente.UnicaVez)
            {
                if (s.ProximoDisparoLocal.Year == AnoCalendario &&
                    s.ProximoDisparoLocal.Month == MesCalendario)
                {
                    AdicionarNoDia(
                        s.ProximoDisparoLocal.Day,
                        new ItemCalendarioMes(
                            UrlMiniatura: imgStoryUrl,
                            IdentificadorInstagram: s.IdentificadorInstagram,
                            Titulo: s.Titulo,
                            HorarioTexto: s.ProximoDisparoLocal.ToString("HH:mm"),
                            TipoMidia: "Story",
                            StatusClasse: "agendado",
                            AncoraAlvo: $"#story-card-{s.Id}"));
                }
            }
            else
            {
                var diasSemStory = InstagramAgendadorService.DeserializarDiasSemana(s.DiasSemanaJson);
                var horariosStory = InstagramAgendadorService.DeserializarHorarios(s.HorariosDiariosJson, ["10:00"]);
                var primeiroHorario = horariosStory.FirstOrDefault() ?? "10:00";

                for (var d = 1; d <= totalDiasNoMes; d++)
                {
                    var dataDia = new DateTime(AnoCalendario, MesCalendario, d);
                    if (dataDia.Date < agora.Date) continue;
                    if (diasSemStory.Contains((int)dataDia.DayOfWeek))
                    {
                        AdicionarNoDia(
                            d,
                            new ItemCalendarioMes(
                                UrlMiniatura: imgStoryUrl,
                                IdentificadorInstagram: s.IdentificadorInstagram,
                                Titulo: s.Titulo,
                                HorarioTexto: primeiroHorario,
                                TipoMidia: "Story",
                                StatusClasse: "agendado",
                                AncoraAlvo: $"#story-card-{s.Id}"));
                    }
                }
            }
        }

        foreach (var kv in mapaDias)
        {
            kv.Value.Sort((a, b) => string.CompareOrdinal(a.HorarioTexto, b.HorarioTexto));
        }

        CalendarioPorDia = mapaDias;
    }
}
