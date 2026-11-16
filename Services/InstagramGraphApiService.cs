using System.Text.Json;
using InstagramAgendador.Web.Models;

namespace InstagramAgendador.Web.Services;

public sealed record ResultadoPublicacaoMeta(
    bool Sucesso,
    string? CreationId,
    string? MediaId,
    string? Erro);

public sealed record ResultadoTesteConexaoMeta(
    bool Sucesso,
    string? UsernameRetornado,
    string? NomeContaRetornado,
    string Mensagem);

public sealed class InstagramGraphApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InstagramGraphApiService> _logger;

    public InstagramGraphApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<InstagramGraphApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ResultadoTesteConexaoMeta> TestarConexaoContaAsync(
        InstagramContaCadastro conta,
        CancellationToken cancellationToken = default)
    {
        var accountId = (conta.AccountIdMeta ?? string.Empty).Trim();
        var accessToken = (conta.AccessTokenMeta ?? string.Empty).Trim();
        var baseUrl = ResolverBaseUrlMeta(conta.GraphApiBaseUrl, accessToken);

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return new ResultadoTesteConexaoMeta(
                false,
                null,
                null,
                "Account ID ou Access Token não preenchidos para este cadastro de Instagram.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));

            var url =
                $"{baseUrl}/{Uri.EscapeDataString(accountId)}?fields=id,username,name&access_token={Uri.EscapeDataString(accessToken)}";

            using var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                var erro = ExtrairErroMeta(body, (int)response.StatusCode);
                return new ResultadoTesteConexaoMeta(false, null, null, erro);
            }

            using var doc = JsonDocument.Parse(body);
            var username = doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;

            var handle = !string.IsNullOrWhiteSpace(username) ? $"@{username.TrimStart('@')}" : conta.IdentificadorInstagram;
            return new ResultadoTesteConexaoMeta(
                true,
                username,
                name,
                $"Conexão validada com sucesso na Meta Graph API para {handle} ({name ?? accountId}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoTesteConexaoMeta(false, null, null, $"Erro ao testar conexão: {ex.Message}");
        }
    }

    public Task<ResultadoPublicacaoMeta> PublicarFeedImagemAsync(
        InstagramContaCadastro conta,
        string urlImagemPublica,
        string legenda,
        CancellationToken cancellationToken = default) =>
        PublicarContainerImagemInternoAsync(
            conta,
            urlImagemPublica,
            legenda,
            ehStory: false,
            cancellationToken);

    public Task<ResultadoPublicacaoMeta> PublicarStoryImagemAsync(
        InstagramContaCadastro conta,
        string urlImagemPublica,
        CancellationToken cancellationToken = default) =>
        PublicarContainerImagemInternoAsync(
            conta,
            urlImagemPublica,
            legenda: null,
            ehStory: true,
            cancellationToken);

    private async Task<ResultadoPublicacaoMeta> PublicarContainerImagemInternoAsync(
        InstagramContaCadastro conta,
        string urlImagem,
        string? legenda,
        bool ehStory,
        CancellationToken cancellationToken = default)
    {
        var accountId = (conta.AccountIdMeta ?? string.Empty).Trim();
        var accessToken = (conta.AccessTokenMeta ?? string.Empty).Trim();
        var baseUrl = ResolverBaseUrlMeta(conta.GraphApiBaseUrl, accessToken);

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return new ResultadoPublicacaoMeta(
                false,
                null,
                null,
                $"As credenciais da conta {conta.IdentificadorInstagram} (Account ID e Access Token) não estão configuradas.");
        }

        if (string.IsNullOrWhiteSpace(urlImagem) ||
            !Uri.TryCreate(urlImagem.Trim(), UriKind.Absolute, out var imagemUri) ||
            (imagemUri.Scheme != Uri.UriSchemeHttps && imagemUri.Scheme != Uri.UriSchemeHttp))
        {
            return new ResultadoPublicacaoMeta(
                false,
                null,
                null,
                "A URL pública da imagem é inválida ou inacessível para o Instagram.");
        }

        var legendaLimpa = (legenda ?? string.Empty).Trim();
        if (legendaLimpa.Length > 2200)
        {
            legendaLimpa = legendaLimpa[..2200];
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));

            var createEndpoint = $"{baseUrl}/{Uri.EscapeDataString(accountId)}/media";
            var formFields = new Dictionary<string, string>
            {
                ["image_url"] = imagemUri.AbsoluteUri,
                ["access_token"] = accessToken
            };

            if (ehStory)
            {
                formFields["media_type"] = "STORIES";
            }
            else if (!string.IsNullOrWhiteSpace(legendaLimpa))
            {
                formFields["caption"] = legendaLimpa;
            }

            using var createContent = new FormUrlEncodedContent(formFields);
            using var createResponse = await client.PostAsync(createEndpoint, createContent, timeout.Token);
            var createBody = await createResponse.Content.ReadAsStringAsync(timeout.Token);

            if (!createResponse.IsSuccessStatusCode)
            {
                var erroCreate = ExtrairErroMeta(createBody, (int)createResponse.StatusCode);
                _logger.LogWarning(
                    "Falha ao criar container no Instagram ({Conta}): {Erro}",
                    conta.IdentificadorInstagram,
                    erroCreate);
                return new ResultadoPublicacaoMeta(false, null, null, erroCreate);
            }

            string? creationId = null;
            using (var createJson = JsonDocument.Parse(createBody))
            {
                if (createJson.RootElement.TryGetProperty("id", out var idProp))
                {
                    creationId = idProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(creationId))
            {
                return new ResultadoPublicacaoMeta(
                    false,
                    null,
                    null,
                    "A API do Instagram não retornou o creation_id do container de mídia.");
            }

            await AguardarContainerProntoAsync(client, baseUrl, creationId, accessToken, timeout.Token);

            var publishEndpoint = $"{baseUrl}/{Uri.EscapeDataString(accountId)}/media_publish";
            using var publishContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["creation_id"] = creationId,
                ["access_token"] = accessToken
            });

            using var publishResponse = await client.PostAsync(publishEndpoint, publishContent, timeout.Token);
            var publishBody = await publishResponse.Content.ReadAsStringAsync(timeout.Token);

            if (!publishResponse.IsSuccessStatusCode)
            {
                var erroPublish = ExtrairErroMeta(publishBody, (int)publishResponse.StatusCode);
                _logger.LogWarning(
                    "Falha ao publicar container {CreationId} no Instagram ({Conta}): {Erro}",
                    creationId,
                    conta.IdentificadorInstagram,
                    erroPublish);
                return new ResultadoPublicacaoMeta(false, creationId, null, erroPublish);
            }

            string? mediaId = null;
            using (var publishJson = JsonDocument.Parse(publishBody))
            {
                if (publishJson.RootElement.TryGetProperty("id", out var mediaIdProp))
                {
                    mediaId = mediaIdProp.GetString();
                }
            }

            _logger.LogInformation(
                "Publicação concluída para {Conta}. CreationId: {CreationId}; MediaId: {MediaId}.",
                conta.IdentificadorInstagram,
                creationId,
                mediaId);

            return new ResultadoPublicacaoMeta(true, creationId, mediaId, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResultadoPublicacaoMeta(
                false,
                null,
                null,
                "Tempo limite excedido (45s) ao comunicar com a API Oficial do Instagram/Meta.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erro inesperado ao publicar no Instagram ({Conta}).", conta.IdentificadorInstagram);
            return new ResultadoPublicacaoMeta(false, null, null, ex.Message);
        }
    }

    private static async Task AguardarContainerProntoAsync(
        HttpClient client,
        string baseUrl,
        string creationId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var statusUrl =
            $"{baseUrl}/{Uri.EscapeDataString(creationId)}?fields=status_code&access_token={Uri.EscapeDataString(accessToken)}";

        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            using var response = await client.GetAsync(statusUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status_code", out var statusProp))
            {
                return;
            }

            var statusCode = statusProp.GetString();
            if (string.Equals(statusCode, "FINISHED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(statusCode, "PUBLISHED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(statusCode, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(statusCode, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static string ExtrairErroMeta(string corpoJson, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(corpoJson))
        {
            return $"Erro HTTP {statusCode} na API Oficial do Instagram/Meta.";
        }

        try
        {
            using var doc = JsonDocument.Parse(corpoJson);
            if (doc.RootElement.TryGetProperty("error", out var errorObj) &&
                errorObj.ValueKind == JsonValueKind.Object)
            {
                var msg = errorObj.TryGetProperty("message", out var m) ? m.GetString() : null;
                var userMsg = errorObj.TryGetProperty("error_user_msg", out var um) ? um.GetString() : null;
                var code = errorObj.TryGetProperty("code", out var c) ? c.ToString() : null;

                var detalhe = !string.IsNullOrWhiteSpace(userMsg)
                    ? $"{msg} ({userMsg})"
                    : msg;
                if (!string.IsNullOrWhiteSpace(detalhe))
                {
                    if (code == "200" && detalhe.Contains("API access blocked", StringComparison.OrdinalIgnoreCase))
                    {
                        detalhe += " — Causa na Meta: (1) Para publicar STORIES via API, a conta do Instagram precisa estar como 'Empresarial (Business)' e não 'Criador de Conteúdo (Creator)' no app do Instagram; (2) Verifique se o Access Token foi gerado com a permissão 'instagram_content_publish' marcada; ou (3) Se o App na Meta estiver em Modo Desenvolvimento, a conta precisa aceitar o convite de Testador.";
                    }

                    return string.IsNullOrWhiteSpace(code)
                        ? $"HTTP {statusCode}: {detalhe}"
                        : $"HTTP {statusCode} [Código Meta {code}]: {detalhe}";
                }
            }
        }
        catch (JsonException)
        {
        }

        var truncado = corpoJson.Length > 400 ? corpoJson[..400] : corpoJson;
        return $"HTTP {statusCode}: {truncado}";
    }

    private static string ResolverBaseUrlMeta(string? baseUrlConfigurada, string accessToken)
    {
        if (accessToken.StartsWith("IGAA", StringComparison.OrdinalIgnoreCase) ||
            accessToken.StartsWith("IGQ", StringComparison.OrdinalIgnoreCase))
        {
            return "https://graph.instagram.com/v21.0";
        }

        return string.IsNullOrWhiteSpace(baseUrlConfigurada)
            ? "https://graph.facebook.com/v21.0"
            : baseUrlConfigurada.Trim().TrimEnd('/');
    }
}
