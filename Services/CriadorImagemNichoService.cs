using System.Net.Http.Headers;
using System.Text;

namespace InstagramAgendador.Web.Services;

public sealed record SugestaoCriativaNicho(
    string Nicho,
    string PromptOriginal,
    string PromptVisualOtimizado,
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
    private readonly ILogger<CriadorImagemNichoService> _logger;

    private static readonly Dictionary<string, (string ContextoIngles, string SeloPadrao, string Hashtags)> MapaNichos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ofertas"] = (
                "commercial product photography, studio lighting, vibrant retail promotion background, ultra detailed, 8k",
                "🔥 OFERTA IMPERDÍVEL",
                "#achadinhos #ofertas #promocao #desconto #compras #oportunidade"
            ),
            ["moda"] = (
                "high fashion editorial photography, trendy outfit, luxury boutique aesthetic, warm cinematic lighting, 8k",
                "✨ TENDÊNCIA DE MODA",
                "#moda #lookdodia #estilo #tendencia #fashion #novacolecao"
            ),
            ["beleza"] = (
                "luxury skincare and beauty product photography, soft pastel glow, clean aesthetic, macro studio shot, 8k",
                "💖 BELEZA & AUTOCUIDADO",
                "#beleza #skincare #estetica #autocuidado #maquiagem #glow"
            ),
            ["fitness"] = (
                "dynamic fitness workout photography, modern gym lighting, athletic energy, high contrast, 8k",
                "💪 FOCO & RESULTADO",
                "#fitness #treino #academia #saude #hipertrofia #vidasaudavel"
            ),
            ["gastronomia"] = (
                "gourmet food photography, appetizing close-up, warm restaurant lighting, fresh ingredients, shallow depth of field, 8k",
                "🍔 SABOR IRRESISTÍVEL",
                "#gastronomia #delivery #comida #instafood #delicia #restaurante"
            ),
            ["tecnologia"] = (
                "futuristic tech gadget photography, sleek neon rim lighting, modern setup, crisp details, 8k",
                "🚀 INOVAÇÃO & TECH",
                "#tecnologia #tech #gadgets #inovacao #setup #eletronicos"
            ),
            ["automotivo"] = (
                "professional automotive and motorcycle photography, dramatic asphalt reflections, cinematic lighting, 8k",
                "🏍️ PAIXÃO SOBRE RODAS",
                "#motos #automotivo #scooter #duasrodas #velocidade #oficina"
            ),
            ["imoveis"] = (
                "luxury modern architecture and interior design photography, natural sunlight, wide angle, architectural digest style, 8k",
                "🏡 SEU NOVO ESPAÇO",
                "#imoveis #arquitetura #decoracao #casa #investimento #morarbem"
            ),
            ["negocios"] = (
                "modern executive workspace and finance aesthetic, clean minimalist lighting, professional corporate style, 8k",
                "📈 DICA DE NEGÓCIOS",
                "#negocios #empreendedorismo #financas #sucesso #marketingdigital #vendas"
            ),
            ["motivacional"] = (
                "inspiring cinematic sunrise landscape, dramatic golden hour clouds, peaceful luxury lifestyle aesthetic, 8k",
                "✨ INSPIRAÇÃO DO DIA",
                "#motivacao #foco #disciplina #mentalidade #sucesso #inspiracao"
            )
        };

    public CriadorImagemNichoService(
        IHttpClientFactory httpClientFactory,
        ILogger<CriadorImagemNichoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public SugestaoCriativaNicho GerarSugestaoCriativa(
        string nicho,
        string promptUsuario,
        string formato,
        string? identificadorInstagram = null,
        string? tituloCustomizado = null,
        string? seloCustomizado = null,
        string? subtituloCustomizado = null)
    {
        var nichoChave = NormalizarChaveNicho(nicho);
        var promptLimpo = (promptUsuario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(promptLimpo))
        {
            promptLimpo = "Destaque especial para clientes exigentes";
        }

        var infoNicho = MapaNichos.TryGetValue(nichoChave, out var encontrado)
            ? encontrado
            : (
                ContextoIngles: "professional commercial photography, cinematic studio lighting, ultra detailed, high resolution, 8k",
                SeloPadrao: "✨ DESTAQUE ESPECIAL",
                Hashtags: "#instagram #novidades #destaque #qualidade #exclusivo"
            );

        var ehVertical = string.Equals(formato, "Stories", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formato, "StoryRecorrente", StringComparison.OrdinalIgnoreCase);

        var largura = 1080;
        var altura = ehVertical ? 1920 : 1080;

        var seed = Random.Shared.Next(10000, 999999);
        var promptVisual =
            $"{promptLimpo}, {infoNicho.ContextoIngles}, no text, no watermark, clean composition, photorealistic";

        var promptEncoded = Uri.EscapeDataString(promptVisual);
        var urlPollinations =
            $"https://image.pollinations.ai/prompt/{promptEncoded}?width={largura}&height={altura}&seed={seed}&nologo=true&model=flux";

        var selo = !string.IsNullOrWhiteSpace(seloCustomizado)
            ? seloCustomizado.Trim()
            : infoNicho.SeloPadrao;

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
                : $"Siga {arroba} · Link e informações na Bio!";

        var legenda = GerarLegendaNicho(promptLimpo, selo, arroba, infoNicho.Hashtags);

        return new SugestaoCriativaNicho(
            Nicho: nichoChave,
            PromptOriginal: promptLimpo,
            PromptVisualOtimizado: promptVisual,
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
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(28);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AgendadorPro/2.0)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

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
            _logger.LogWarning(ex, "Falha ao baixar imagem IA principal em {Url}. Usando fallback fotográfico.", urlImagemGerada);
        }

        // Fallback fotográfico rápido caso o provedor IA demore ou oscile
        var seed = Random.Shared.Next(1000, 99999);
        var urlFallback = $"https://picsum.photos/seed/{seed}/{largura}/{altura}.jpg";
        using var respFallback = await client.GetAsync(urlFallback, cancellationToken);
        respFallback.EnsureSuccessStatusCode();
        var bytesFallback = await respFallback.Content.ReadAsByteArrayAsync(cancellationToken);
        return (bytesFallback, "image/jpeg");
    }

    private static string NormalizarChaveNicho(string? nicho)
    {
        var valor = (nicho ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(valor))
        {
            return "ofertas";
        }

        foreach (var key in MapaNichos.Keys)
        {
            if (valor.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return valor;
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

    private static string GerarLegendaNicho(
        string prompt,
        string selo,
        string arroba,
        string hashtags)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{selo}!");
        sb.AppendLine();
        sb.AppendLine($"✨ { char.ToUpperInvariant(prompt[0]) }{ prompt[1..] }");
        sb.AppendLine();
        sb.AppendLine("👉 Gostou? Salve este post para não perder e confira o link na nossa Bio para mais detalhes!");
        if (!string.IsNullOrWhiteSpace(arroba))
        {
            sb.AppendLine($"📲 Acompanhe nosso perfil: {arroba}");
        }
        sb.AppendLine();
        sb.Append(hashtags);
        return sb.ToString();
    }
}
