using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InstagramAgendador.Web.Services;

public sealed record ResultadoGeracaoImagemIa(
    bool Sucesso,
    string? Erro,
    string ProvedorIa,
    string PromptVisualUsado,
    string ImagemBase64DataUrl,
    byte[] BytesImagem,
    string ContentType,
    string SeloTopo,
    string TituloArte,
    string SubtituloArte,
    IReadOnlyList<string> BeneficiosExtraidos,
    string ChamadaCta,
    string BotaoWhatsapp,
    string RodapeTexto,
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

    public async Task<ResultadoGeracaoImagemIa> GerarImagemPorPromptAsync(
        string promptUsuario,
        string formato,
        string? identificadorInstagram = null,
        string? tituloCustomizado = null,
        string? seloCustomizado = null,
        string? subtituloCustomizado = null,
        string? modeloIa = null,
        string? apiKeyManual = null,
        CancellationToken cancellationToken = default)
    {
        var promptOriginal = (promptUsuario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptOriginal))
        {
            throw new ArgumentException("Digite o prompt descrevendo a imagem que deseja criar.");
        }

        var ehVertical = string.Equals(formato, "Stories", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formato, "StoryRecorrente", StringComparison.OrdinalIgnoreCase)
            || promptOriginal.Contains("9:16", StringComparison.OrdinalIgnoreCase)
            || promptOriginal.Contains("1080x1920", StringComparison.OrdinalIgnoreCase);

        var largura = 1080;
        var altura = ehVertical ? 1920 : 1080;

        var modeloEscolhido = string.IsNullOrWhiteSpace(modeloIa)
            ? "flux"
            : modeloIa.Trim().ToLowerInvariant();

        // Extrai metadados caso o prompt seja um briefing estruturado de poster (ex: MAIN HEADER, MAIN VISUAL, BENEFITS, WHATSAPP)
        var (promptCenaVisual, tituloExtraido, subtituloExtraido, beneficios, ctaExtraido, whatsappExtraido, rodapeExtraido) =
            AnalisarPromptEstruturado(promptOriginal);

        var selo = !string.IsNullOrWhiteSpace(seloCustomizado)
            ? seloCustomizado.Trim()
            : !string.IsNullOrWhiteSpace(subtituloExtraido)
                ? subtituloExtraido
                : "✨ DESTAQUE";

        var titulo = !string.IsNullOrWhiteSpace(tituloCustomizado)
            ? tituloCustomizado.Trim()
            : !string.IsNullOrWhiteSpace(tituloExtraido)
                ? tituloExtraido
                : GerarTituloImpacto(promptOriginal);

        var arroba = string.IsNullOrWhiteSpace(identificadorInstagram)
            ? string.Empty
            : identificadorInstagram.Trim();

        var subtitulo = !string.IsNullOrWhiteSpace(subtituloCustomizado)
            ? subtituloCustomizado.Trim()
            : !string.IsNullOrWhiteSpace(ctaExtraido)
                ? ctaExtraido
                : string.IsNullOrWhiteSpace(arroba)
                    ? "Confira todos os detalhes · Link na Bio!"
                    : $"Siga {arroba} · Confira no link da Bio!";

        var legenda = GerarLegendaPorPrompt(promptOriginal, titulo, subtitulo, beneficios, arroba);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        // 1. Se escolheu OpenAI (DALL-E 3 / gpt-image-1) ou informou chave OpenAI
        var openAiKey = !string.IsNullOrWhiteSpace(apiKeyManual) && apiKeyManual.StartsWith("sk-", StringComparison.Ordinal)
            ? apiKeyManual.Trim()
            : _configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (!string.IsNullOrWhiteSpace(openAiKey) &&
            (modeloEscolhido.Contains("dalle", StringComparison.OrdinalIgnoreCase) ||
             modeloEscolhido.Contains("openai", StringComparison.OrdinalIgnoreCase)))
        {
            var dalleBytes = await GerarImagemOpenAiDalleAsync(client, openAiKey, promptOriginal, largura, altura, cancellationToken);
            if (dalleBytes is { Length: > 1024 })
            {
                var b64OpenAi = $"data:image/png;base64,{Convert.ToBase64String(dalleBytes)}";
                return new ResultadoGeracaoImagemIa(
                    Sucesso: true,
                    Erro: null,
                    ProvedorIa: "OpenAI DALL-E 3",
                    PromptVisualUsado: promptOriginal,
                    ImagemBase64DataUrl: b64OpenAi,
                    BytesImagem: dalleBytes,
                    ContentType: "image/png",
                    SeloTopo: selo,
                    TituloArte: titulo,
                    SubtituloArte: subtitulo,
                    BeneficiosExtraidos: beneficios,
                    ChamadaCta: ctaExtraido,
                    BotaoWhatsapp: whatsappExtraido,
                    RodapeTexto: rodapeExtraido,
                    LegendaSugerida: legenda,
                    Largura: largura,
                    Altura: altura);
            }
        }

        // 2. Se escolheu Google Gemini / Imagen ou informou chave Gemini (AIza...)
        var geminiKey = !string.IsNullOrWhiteSpace(apiKeyManual) && apiKeyManual.StartsWith("AIza", StringComparison.Ordinal)
            ? apiKeyManual.Trim()
            : _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (!string.IsNullOrWhiteSpace(geminiKey) &&
            (modeloEscolhido.Contains("gemini", StringComparison.OrdinalIgnoreCase) ||
             modeloEscolhido.Contains("imagen", StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(apiKeyManual) && apiKeyManual.StartsWith("AIza", StringComparison.Ordinal))))
        {
            var geminiBytes = await GerarImagemGoogleGeminiAsync(client, geminiKey, promptOriginal, ehVertical, cancellationToken);
            if (geminiBytes is { Length: > 1024 })
            {
                var b64Gemini = $"data:image/png;base64,{Convert.ToBase64String(geminiBytes)}";
                return new ResultadoGeracaoImagemIa(
                    Sucesso: true,
                    Erro: null,
                    ProvedorIa: "Google Gemini / Imagen 3",
                    PromptVisualUsado: promptOriginal,
                    ImagemBase64DataUrl: b64Gemini,
                    BytesImagem: geminiBytes,
                    ContentType: "image/png",
                    SeloTopo: selo,
                    TituloArte: titulo,
                    SubtituloArte: subtitulo,
                    BeneficiosExtraidos: beneficios,
                    ChamadaCta: ctaExtraido,
                    BotaoWhatsapp: whatsappExtraido,
                    RodapeTexto: rodapeExtraido,
                    LegendaSugerida: legenda,
                    Largura: largura,
                    Altura: altura);
            }
        }

        // 3. Geração via Flux AI (Pollinations) com compactação inteligente de URL para não estourar limite HTTP de 450 caracteres no path
        var seed = Random.Shared.Next(10000, 999999);
        var modelosParaTentar = new[]
        {
            modeloEscolhido is "dalle3" or "gemini" ? "flux" : modeloEscolhido,
            "flux",
            "turbo"
        }.Distinct().ToArray();

        string? ultimoErro = null;

        foreach (var mod in modelosParaTentar)
        {
            var urlFlux = ConstruirUrlPollinationsSegura(promptCenaVisual, largura, altura, seed, mod);
            try
            {
                using var resp = await client.GetAsync(urlFlux, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length > 2048)
                    {
                        var ct = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        var b64 = $"data:{ct};base64,{Convert.ToBase64String(bytes)}";
                        return new ResultadoGeracaoImagemIa(
                            Sucesso: true,
                            Erro: null,
                            ProvedorIa: $"Flux AI ({mod})",
                            PromptVisualUsado: promptCenaVisual,
                            ImagemBase64DataUrl: b64,
                            BytesImagem: bytes,
                            ContentType: ct,
                            SeloTopo: selo,
                            TituloArte: titulo,
                            SubtituloArte: subtitulo,
                            BeneficiosExtraidos: beneficios,
                            ChamadaCta: ctaExtraido,
                            BotaoWhatsapp: whatsappExtraido,
                            RodapeTexto: rodapeExtraido,
                            LegendaSugerida: legenda,
                            Largura: largura,
                            Altura: altura);
                    }
                }
                else
                {
                    ultimoErro = $"HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ultimoErro = ex.Message;
                _logger.LogWarning(ex, "Tentativa com modelo {Modelo} falhou em {Url}", mod, urlFlux);
            }
        }

        throw new InvalidOperationException(
            $"O motor de IA gratuito está temporariamente ocupado ({ultimoErro ?? "timeout"}). Tente clicar novamente em 'Gerar Imagem' ou informe uma chave Gemini/OpenAI.");
    }

    private static string ConstruirUrlPollinationsSegura(
        string promptVisual,
        int largura,
        int altura,
        int seed,
        string modelo)
    {
        // Limpa quebras de linha, markdown e símbolos que quebram a rota de path do Cloudflare/Pollinations
        var limpo = Regex.Replace(promptVisual, @"[\r\n\t]+", " ");
        limpo = Regex.Replace(limpo, @"[\*\#\""\`\[\]\(\)\{\}]+", "");
        limpo = Regex.Replace(limpo, @"\s{2,}", " ").Trim();

        // O path de URL do Pollinations falha se passar de ~380 caracteres antes do Uri.EscapeDataString
        if (limpo.Length > 360)
        {
            limpo = limpo[..360].Trim();
        }

        var encoded = Uri.EscapeDataString(limpo);
        return $"https://image.pollinations.ai/prompt/{encoded}?width={largura}&height={altura}&seed={seed}&nologo=true&model={Uri.EscapeDataString(modelo)}";
    }

    private static (
        string PromptCenaVisual,
        string TituloExtraido,
        string SubtituloExtraido,
        List<string> Beneficios,
        string CtaExtraido,
        string WhatsappExtraido,
        string RodapeExtraido) AnalisarPromptEstruturado(string promptOriginal)
    {
        var texto = promptOriginal;
        var beneficios = new List<string>();
        var titulo = string.Empty;
        var subtitulo = string.Empty;
        var cta = string.Empty;
        var whatsapp = string.Empty;
        var rodape = string.Empty;

        // Detecta marca entre aspas logo no início (ex: called "SCOOTER ACHADINHOS")
        var matchBrand = Regex.Match(texto, @"called\s+\**\x22([^\x22]+)\x22\**", RegexOptions.IgnoreCase);
        if (matchBrand.Success)
        {
            titulo = matchBrand.Groups[1].Value.Trim();
        }

        // Detecta frase abaixo do título ("Below the title, display: ...")
        var matchSub = Regex.Match(texto, @"Below the title[^\x22]*\x22([^\x22]+)\x22", RegexOptions.IgnoreCase);
        if (matchSub.Success)
        {
            subtitulo = matchSub.Groups[1].Value.Trim();
        }

        // Detecta itens numerados 1. "..." 2. "..." 3. "..."
        foreach (Match m in Regex.Matches(texto, @"\d+\.\s*\x22([^\x22]+)\x22"))
        {
            var item = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(item) && beneficios.Count < 4)
            {
                beneficios.Add(item);
            }
        }

        // Detecta CALL TO ACTION
        var matchCta = Regex.Match(texto, @"CALL TO ACTION:[\s\S]*?\x22([^\x22]+)\x22", RegexOptions.IgnoreCase);
        if (matchCta.Success)
        {
            cta = matchCta.Groups[1].Value.Trim();
        }

        // Detecta WHATSAPP SECTION
        var matchWpp = Regex.Match(texto, @"WHATSAPP SECTION:[\s\S]*?\x22([^\x22]+)\x22", RegexOptions.IgnoreCase);
        if (matchWpp.Success)
        {
            whatsapp = matchWpp.Groups[1].Value.Trim();
        }

        // Detecta FOOTER
        var matchFooter = Regex.Match(texto, @"FOOTER:[\s\S]*?\x22([^\x22]+)\x22", RegexOptions.IgnoreCase);
        if (matchFooter.Success)
        {
            rodape = matchFooter.Groups[1].Value.Trim();
        }

        // Extrai MAIN VISUAL + VISUAL IDENTITY se existirem para enviar um prompt fotográfico cirúrgico ao gerador
        var matchMainVisual = Regex.Match(
            texto,
            @"MAIN VISUAL:\**\s*([\s\S]*?)(?:\*\*PROMOTIONAL|\*\*CALL|\*\*WHATSAPP|\*\*FOOTER|\*\*IMPORTANT|$)",
            RegexOptions.IgnoreCase);

        var matchVisualIdentity = Regex.Match(
            texto,
            @"VISUAL IDENTITY:\**\s*([\s\S]*?)(?:\*\*MAIN HEADER|\*\*MAIN VISUAL|$)",
            RegexOptions.IgnoreCase);

        if (matchMainVisual.Success)
        {
            var visual = matchMainVisual.Groups[1].Value.Trim();
            var identity = matchVisualIdentity.Success ? matchVisualIdentity.Groups[1].Value.Trim() : string.Empty;
            var combinado = $"{visual}. {identity}. Professional commercial photography, 8k, ultra detailed, dramatic studio lighting, no text";
            return (combinado, titulo, subtitulo, beneficios, cta, whatsapp, rodape);
        }

        // Caso seja um prompt livre comum
        var cenaLivre = texto.Length > 340
            ? $"{texto[..340]}, professional commercial photography, ultra detailed, 8k"
            : $"{texto}, professional commercial photography, ultra detailed, cinematic lighting, 8k";

        return (cenaLivre, titulo, subtitulo, beneficios, cta, whatsapp, rodape);
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
                prompt = prompt.Length > 3800 ? prompt[..3800] : prompt,
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

    private static async Task<byte[]?> GerarImagemGoogleGeminiAsync(
        HttpClient client,
        string apiKey,
        string prompt,
        bool ehVertical,
        CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-002:predict?key={Uri.EscapeDataString(apiKey.Trim())}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                instances = new[] { new { prompt } },
                parameters = new
                {
                    sampleCount = 1,
                    aspectRatio = ehVertical ? "9:16" : "1:1"
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var resp = await client.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (doc.RootElement.TryGetProperty("predictions", out var preds) &&
            preds.GetArrayLength() > 0 &&
            preds[0].TryGetProperty("bytesBase64Encoded", out var b64Prop))
        {
            var b64 = b64Prop.GetString();
            return string.IsNullOrWhiteSpace(b64) ? null : Convert.FromBase64String(b64);
        }

        return null;
    }

    private static string GerarTituloImpacto(string prompt)
    {
        var limpo = Regex.Replace(prompt.Trim(), @"[\r\n\*]+", " ");
        if (limpo.Length <= 52)
        {
            return limpo.ToUpperInvariant();
        }

        var cortado = limpo[..52];
        var ultimoEspaco = cortado.LastIndexOf(' ');
        if (ultimoEspaco > 18)
        {
            cortado = cortado[..ultimoEspaco];
        }

        return $"{cortado.ToUpperInvariant()}...";
    }

    private static string GerarLegendaPorPrompt(
        string promptOriginal,
        string titulo,
        string subtitulo,
        IReadOnlyList<string> beneficios,
        string arroba)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🔥 {titulo}");
        if (!string.IsNullOrWhiteSpace(subtitulo))
        {
            sb.AppendLine($"✨ {subtitulo}");
        }
        sb.AppendLine();

        if (beneficios.Count > 0)
        {
            foreach (var b in beneficios)
            {
                sb.AppendLine($"✅ {b}");
            }
            sb.AppendLine();
        }
        else
        {
            var resumo = promptOriginal.Length > 280 ? $"{promptOriginal[..280]}..." : promptOriginal;
            sb.AppendLine(resumo);
            sb.AppendLine();
        }

        sb.AppendLine("👉 Entre no nosso grupo e confira o link na Bio para aproveitar as melhores ofertas!");
        if (!string.IsNullOrWhiteSpace(arroba))
        {
            sb.AppendLine($"📲 Siga: {arroba}");
        }
        sb.AppendLine();
        sb.Append("#scooterachadinhos #scooter #nmax #ofertas #achadinhos #acessorios #descontos #motociclismo");
        return sb.ToString();
    }
}
