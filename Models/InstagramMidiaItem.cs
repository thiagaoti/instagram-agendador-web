using System.ComponentModel.DataAnnotations;

namespace InstagramAgendador.Web.Models;

public class InstagramMidiaItem
{
    public int Id { get; set; }

    public int PostAgendadoId { get; set; }

    public int Ordem { get; set; }

    [Required]
    public byte[] Conteudo { get; set; } = [];

    [Required]
    [StringLength(80)]
    public string ContentType { get; set; } = "image/jpeg";

    public DateTimeOffset CriadoEmUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ItemMidiaCarrosselMeta
{
    public string Url { get; set; } = string.Empty;
    public bool EhVideo { get; set; }
}
