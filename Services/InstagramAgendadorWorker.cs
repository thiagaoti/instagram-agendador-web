namespace InstagramAgendador.Web.Services;

public sealed class InstagramAgendadorWorker : BackgroundService
{
    private static readonly TimeSpan IntervaloVerificacao = TimeSpan.FromSeconds(30);
    private readonly InstagramAgendadorService _agendadorService;
    private readonly ILogger<InstagramAgendadorWorker> _logger;

    public InstagramAgendadorWorker(
        InstagramAgendadorService agendadorService,
        ILogger<InstagramAgendadorWorker> logger)
    {
        _agendadorService = agendadorService;
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
}
