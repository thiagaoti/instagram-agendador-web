using System.ComponentModel.DataAnnotations;

namespace InstagramAgendador.Web.Models;

public static class ModoStoryRecorrente
{
    public const string RecorrenteDiario = "RecorrenteDiario";
    public const string UnicaVez = "UnicaVez";
}

public static class StatusStoryRecorrente
{
    public const string Ativo = "Ativo";
    public const string Pausado = "Pausado";
    public const string Concluido = "Concluido";
    public const string Falha = "Falha";
}

public class InstagramStoryRecorrente
{
    public int Id { get; set; }

    public int InstagramContaId { get; set; }

    [Required]
    [StringLength(100)]
    public string IdentificadorInstagram { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Titulo { get; set; } = "Story Programado";

    [Required]
    public byte[] ConteudoImagem { get; set; } = [];

    [Required]
    [StringLength(80)]
    public string ContentTypeImagem { get; set; } = "image/jpeg";

    [StringLength(500)]
    public string? BaseUrlPublica { get; set; }

    [Required]
    [StringLength(30)]
    public string ModoAgendamento { get; set; } = ModoStoryRecorrente.RecorrenteDiario;

    [Required]
    [StringLength(500)]
    public string HorariosDiariosJson { get; set; } = "[\"10:00\",\"19:00\"]";

    [Required]
    [StringLength(150)]
    public string DiasSemanaJson { get; set; } = "[0,1,2,3,4,5,6]";

    public DateTime? DataHoraUnicaLocal { get; set; }

    public DateTime ProximoDisparoLocal { get; set; }

    public DateTimeOffset ProximoDisparoUtc { get; set; }

    public bool Ativo { get; set; } = true;

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = StatusStoryRecorrente.Ativo;

    public int TotalPublicacoes { get; set; }

    public int TentativasNoHorarioAtual { get; set; }

    [StringLength(120)]
    public string? UltimoMediaIdMeta { get; set; }

    [StringLength(1500)]
    public string? UltimoErro { get; set; }

    public DateTimeOffset? UltimaPublicacaoEmUtc { get; set; }

    public DateTimeOffset CriadoEmUtc { get; set; } = DateTimeOffset.UtcNow;
}
