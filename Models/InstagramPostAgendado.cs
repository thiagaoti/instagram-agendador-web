using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InstagramAgendador.Web.Models;

public static class StatusPostInstagram
{
    public const string Agendado = "Agendado";
    public const string Publicando = "Publicando";
    public const string Publicado = "Publicado";
    public const string Falha = "Falha";
    public const string Cancelado = "Cancelado";
}

public static class TipoMidiaInstagram
{
    public const string Feed = "Feed";
    public const string FeedVideo = "FeedVideo";
    public const string FeedCarrossel = "FeedCarrossel";
    public const string Stories = "Stories";
    public const string FeedEStories = "FeedEStories";
}

public class InstagramPostAgendado
{
    public int Id { get; set; }

    public int InstagramContaId { get; set; }

    [Required]
    [StringLength(100)]
    public string IdentificadorInstagram { get; set; } = string.Empty;

    [Required]
    [StringLength(180)]
    public string Titulo { get; set; } = string.Empty;

    [StringLength(2200)]
    public string Legenda { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string TipoPublicacao { get; set; } = TipoMidiaInstagram.Feed;

    public byte[]? ConteudoImagem { get; set; }

    [StringLength(80)]
    public string? ContentTypeImagem { get; set; }

    public string? UrlsCarrosselJson { get; set; }

    [NotMapped]
    public bool EhVideo =>
        (ContentTypeImagem ?? string.Empty).StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TipoPublicacao, TipoMidiaInstagram.FeedVideo, StringComparison.OrdinalIgnoreCase);

    [NotMapped]
    public bool EhCarrossel =>
        string.Equals(TipoPublicacao, TipoMidiaInstagram.FeedCarrossel, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(UrlsCarrosselJson);

    [StringLength(1000)]
    public string? UrlImagemExterna { get; set; }

    [StringLength(500)]
    public string? BaseUrlPublica { get; set; }

    public DateTime HorarioProgramadoLocal { get; set; }

    public DateTimeOffset HorarioProgramadoUtc { get; set; }

    public bool RecorrenteSemanal { get; set; }

    [StringLength(150)]
    public string? DiasSemanaJson { get; set; }

    [StringLength(50)]
    public string? HorarioFixoSemanal { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = StatusPostInstagram.Agendado;

    [StringLength(120)]
    public string? CreationIdMeta { get; set; }

    [StringLength(120)]
    public string? MediaIdMeta { get; set; }

    public int Tentativas { get; set; }

    [StringLength(1500)]
    public string? UltimoErro { get; set; }

    public DateTimeOffset CriadoEmUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PublicadoEmUtc { get; set; }
}
