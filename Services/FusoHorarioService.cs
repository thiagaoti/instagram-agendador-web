namespace InstagramAgendador.Web.Services;

public static class FusoHorarioService
{
    private static readonly TimeZoneInfo FusoHorario = ObterFusoHorario();

    public static string Identificador => FusoHorario.Id;

    public static DateTimeOffset AgoraLocal =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FusoHorario);

    public static DateTimeOffset ConverterParaLocal(DateTimeOffset instanteUtc) =>
        TimeZoneInfo.ConvertTime(instanteUtc.ToUniversalTime(), FusoHorario);

    public static DateTimeOffset ConverterParaUtc(DateTime horarioLocal)
    {
        var horarioSemFuso = DateTime.SpecifyKind(horarioLocal, DateTimeKind.Unspecified);
        var instanteUtc = TimeZoneInfo.ConvertTimeToUtc(horarioSemFuso, FusoHorario);
        return new DateTimeOffset(instanteUtc, TimeSpan.Zero);
    }

    private static TimeZoneInfo ObterFusoHorario()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
