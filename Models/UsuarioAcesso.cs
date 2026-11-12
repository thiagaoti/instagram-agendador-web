using System.ComponentModel.DataAnnotations;

namespace InstagramAgendador.Web.Models;

public class UsuarioAcesso
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string NomeExibicao { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string Login { get; set; } = string.Empty;

    [Required]
    [StringLength(400)]
    public string SenhaHash { get; set; } = string.Empty;

    public int? InstagramContaId { get; set; }

    [StringLength(120)]
    public string? IdentificadorInstagramVinculado { get; set; }

    public bool Administrador { get; set; } = true;

    public bool Ativo { get; set; } = true;

    public DateTimeOffset CriadoEmUtc { get; set; } = DateTimeOffset.UtcNow;
}
