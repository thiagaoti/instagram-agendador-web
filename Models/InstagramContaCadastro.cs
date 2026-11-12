using System.ComponentModel.DataAnnotations;

namespace InstagramAgendador.Web.Models;

public class InstagramContaCadastro
{
    public int Id { get; set; }

    public int? UsuarioAcessoId { get; set; }

    [StringLength(250)]
    public string? LoginUsuarioVinculado { get; set; }

    [Required]
    [StringLength(120)]
    public string NomeCadastro { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string IdentificadorInstagram { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string AccountIdMeta { get; set; } = string.Empty;

    [Required]
    public string AccessTokenMeta { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com/v21.0";

    [StringLength(500)]
    public string? BaseUrlPublica { get; set; }

    [Required]
    [StringLength(500)]
    public string HorariosFeedJson { get; set; } = "[\"09:00\",\"13:00\",\"18:00\"]";

    [Required]
    [StringLength(500)]
    public string HorariosStoryJson { get; set; } = "[\"10:00\",\"19:00\"]";

    public bool PublicacaoAutomaticaAtiva { get; set; } = true;

    public bool Ativo { get; set; } = true;

    public bool Padrao { get; set; }

    [StringLength(500)]
    public string? UltimoTesteStatus { get; set; }

    public DateTimeOffset? UltimoTesteEmUtc { get; set; }

    public DateTimeOffset CriadoEmUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset AtualizadoEmUtc { get; set; } = DateTimeOffset.UtcNow;
}
