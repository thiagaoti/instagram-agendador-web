namespace InstagramAgendador.Web.Services;

public sealed class InstagramAgendadorWorker : BackgroundService
{
    private static readonly TimeSpan IntervaloVerificacao = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IntervaloKeepAlive = TimeSpan.FromMinutes(8);
    private readonly InstagramAgendadorService _agendadorService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InstagramAgendadorWorker> _logger;
    private DateTimeOffset _ultimoPingUtc = DateTimeOffset.MinValue;

    public InstagramAgendadorWorker(
        InstagramAgendadorService agendadorService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<InstagramAgendadorWorker> logger)
    {
        _agendadorService = agendadorService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Worker do Sistema de Agendamento Instagram iniciado no fuso {Fuso} ({AgoraLocal}).",
            FusoHorarioService.Identificador,
            FusoHorarioService.AgoraLocal.DateTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publicados = await _agendadorService.ProcessarFilaVencidaAsync(stoppingToken);
                if (publicados > 0)
                {
                    _logger.LogInformation(
                        "{Quantidade} publicação(ões) agendada(s) enviada(s) ao Instagram com sucesso.",
                        publicados);
                }

                await ExecutarKeepAliveRenderAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no processamento automático da fila do Instagram.");
            }

            try
            {
                await Task.Delay(IntervaloVerificacao, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExecutarKeepAliveRenderAsync(CancellationToken cancellationToken)
    {
        var agoraUtc = DateTimeOffset.UtcNow;
        if (agoraUtc - _ultimoPingUtc < IntervaloKeepAlive)
        {
            return;
        }

        _ultimoPingUtc = agoraUtc;

        var urlBase = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")
            ?? _configuration["KeepAlive:PublicUrl"];

        if (string.IsNullOrWhiteSpace(urlBase))
        {
            var contas = await _agendadorService.ListarContasAsync(cancellationToken);
            urlBase = contas
                .Select(c => c.BaseUrlPublica)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u) && !u.Contains("localhost", StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return;
        }

        var urlPing = $"{urlBase.Trim().TrimEnd('/')}/ping";
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.GetAsync(urlPing, cancellationToken);
            _logger.LogDebug("Keep-alive ping enviado para {UrlPing} -> {StatusCode}", urlPing, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Falha silenciosa no auto-ping para {UrlPing}", urlPing);
        }
    }
}
