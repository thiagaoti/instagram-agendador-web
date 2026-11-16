using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace InstagramAgendador.Web.Services;

public sealed record SugestaoCriativaPrompt(
    string PromptOriginal,
    string PromptVisualOtimizado,
    string ProvedorIa,
    string UrlImagemGerada,
    string SeloTopo,
    string TituloArte,
    string SubtituloArte,
    string LegendaSugerida,
    int Largura,
    int Altura);

public sealed class CriadorImagemNichoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CriadorImagemNichoService> _logger;

    public CriadorImagemNichoService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CriadorImagemNichoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public SugestaoCriativaPrompt GerarSugestaoPorPrompt(
        string promptUsuario,
        string formato,
        string? identificadorInstagram = null,
        string? tituloCustomizado = null,
        string? seloCustomizado = null,
        string? subtituloCustomizado = null,
        string? modeloIa = null)
    {
        var promptLimpo = (promptUsuario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptLimpo))
        {
            promptLimpo = "Fotografia profissional de alta qualidade para Instagram";
        }

        var ehVertical = string.Equals(formato, "Stories", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formato, "StoryRecorrente", StringComparison.OrdinalIgnoreCase);

        var largura = 1080;
        var altura = ehVertical ? 1920 : 1080;

        var modeloEscolhido = string.IsNullOrWhiteSpace(modeloIa) ? "flux" : modeloIa.Trim().ToLowerInvariant();
        var seed = Random.Shared.Next(10000, 999999);

        var promptVisual =
            $"{promptLimpo}, professional photography, ultra detailed, cinematic lighting, high resolution, 8k, photorealistic";

        var promptEncoded = Uri.EscapeDataString(promptVisual);
        var urlPollinations =
            $"https://image.pollinations.ai/prompt/{promptEncoded}?width={largura}&height={altura}&seed={seed}&nologo=true&model={Uri.EscapeDataString(modeloEscolhido)}";

        var selo = !string.IsNullOrWhiteSpace(seloCustomizado)
            ? seloCustomizado.Trim()
            : "✨ DESTAQUE";

        var titulo = !string.IsNullOrWhiteSpace(tituloCustomizado)
            ? tituloCustomizado.Trim()
            : GerarTituloImpacto(promptLimpo);

        var arroba = string.IsNullOrWhiteSpace(identificadorInstagram)
            ? string.Empty
            : identificadorInstagram.Trim();

        var subtitulo = !string.IsNullOrWhiteSpace(subtituloCustomizado)
            ? subtituloCustomizado.Trim()
            : string.IsNullOrWhiteSpace(arroba)
                ? "Confira todos os detalhes · Link na Bio!"
                : $"Siga {arroba} · Confira no link da Bio!";

        var legenda = GerarLegendaPorPrompt(promptLimpo, arroba);

        return new SugestaoCriativaPrompt(
            PromptOriginal: promptLimpo,
            PromptVisualOtimizado: promptVisual,
            ProvedorIa: $"Flux AI ({modeloEscolhido})",
            UrlImagemGerada: urlPollinations,
            SeloTopo: selo,
            TituloArte: titulo,
            SubtituloArte: subtitulo,
            LegendaSugerida: legenda,
            Largura: largura,
            Altura: altura);
    }

    public async Task<(byte[] Bytes, string ContentType)> BaixarOuGerarImagemBytesAsync(
        string urlImagemGerada,
        int largura,
        int altura,
        string? promptOriginal = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(35);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AgendadorPro/2.0)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        // 1. Se houver OPENAI_API_KEY configurada e o usuário escolheu DALL-E 3, permite gerar via OpenAI
        var openAiKey = _configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiKey) &&
            urlImagemGerada.Contains("model=dalle3", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(promptOriginal))
        {
            try
            {
                var dalleBytes = await GerarImagemOpenAiDalleAsync(client, openAiKey, promptOriginal, largura, altura, cancellationToken);
                if (dalleBytes is { Length: > 1024 })
                {
                    return (dalleBytes, "image/png");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao gerar via OpenAI DALL-E, usando Flux AI gratuito.");
            }
        }

        // 2. Geração via Flux AI / Pollinations (100% Gratuito, sem API Key)
        try
        {
            using var resp = await client.GetAsync(urlImagemGerada, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length > 1024)
                {
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    return (bytes, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao baixar imagem IA principal em {Url}. Usando fallback.", urlImagemGerada);
        }

        var seed = Random.Shared.Next(1000, 99999);
        var urlFallback = $"https://picsum.photos/seed/{seed}/{largura}/{altura}.jpg";
        using var respFallback = await client.GetAsync(urlFallback, cancellationToken);
        respFallback.EnsureSuccessStatusCode();
        var bytesFallback = await respFallback.Content.ReadAsByteArrayAsync(cancellationToken);
        return (bytesFallback, "image/jpeg");
    }

    private static async Task<byte[]?> GerarImagemOpenAiDalleAsync(
        HttpClient client,
        string apiKey,
        string prompt,
        int largura,
        int altura,
        CancellationToken cancellationToken)
    {
        var size = altura > largura ? "1024x1792" : "1024x1024";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        req.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = "dall-e-3",
                prompt,
                n = 1,
                size,
                response_format = "b64_json"
            }),
            Encoding.UTF8,
            "application/json");

        using var resp = await client.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var b64 = doc.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString();
        return string.IsNullOrWhiteSpace(b64) ? null : Convert.FromBase64String(b64);
    }

    private static string GerarTituloImpacto(string prompt)
    {
        var limpo = prompt.Trim();
        if (limpo.Length <= 58)
        {
            return limpo.ToUpperInvariant();
        }

        var cortado = limpo[..58];
        var ultimoEspaco = cortado.LastIndexOf(' ');
        if (ultimoEspaco > 20)
        {
            cortado = cortado[..ultimoEspaco];
        }

        return $"{cortado.ToUpperInvariant()}...";
    }

    private static string GerarLegendaPorPrompt(string prompt, string arroba)
    {
        var palavrasHashtags = prompt
            .Split([' ', ',', '.', '!', '?', '-', '/', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(p => p.Length >= 4)
            .Distinct()
            .Take(5)
            .Select(p => $"#{p}")
            .ToList();

        if (palavrasHashtags.Count == 0)
        {
            palavrasHashtags.AddRange(["#destaque", "#novidades", "#instagram"]);
        }
        else
        {
            palavrasHashtags.Add("#destaque");
            palavrasHashtags.Add("#novidades");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"✨ {char.ToUpperInvariant(prompt[0])}{prompt[1..]}");
        sb.AppendLine();
        sb.AppendLine("👉 Gostou? Salve este post para não perder e confira o link na nossa Bio para mais detalhes!");
        if (!string.IsNullOrWhiteSpace(arroba))
        {
            sb.AppendLine($"📲 Siga nosso perfil: {arroba}");
        }
        sb.AppendLine();
        sb.Append(string.Join(' ', palavrasHashtags));
        return sb.ToString();
    }
}
