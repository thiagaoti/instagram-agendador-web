using System.ComponentModel.DataAnnotations;

namespace InstagramAgendador.Web.Models;

public class ConfiguracaoMetaOAuth
{
    public int Id { get; set; }

    [StringLength(120)]
    public string AppId { get; set; } = string.Empty;

    [StringLength(250)]
    public string AppSecret { get; set; } = string.Empty;

    public DateTimeOffset AtualizadoEmUtc { get; set; } = DateTimeOffset.UtcNow;
}
