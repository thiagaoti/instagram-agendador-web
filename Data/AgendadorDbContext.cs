using InstagramAgendador.Web.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InstagramAgendador.Web.Data;

public class AgendadorDbContext : DbContext, IDataProtectionKeyContext
{
    public AgendadorDbContext(DbContextOptions<AgendadorDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<ConfiguracaoMetaOAuth> ConfiguracoesMeta => Set<ConfiguracaoMetaOAuth>();
    public DbSet<InstagramContaCadastro> ContasInstagram => Set<InstagramContaCadastro>();
    public DbSet<UsuarioAcesso> UsuariosAcesso => Set<UsuarioAcesso>();
    public DbSet<InstagramPostAgendado> PostsAgendados => Set<InstagramPostAgendado>();
    public DbSet<InstagramStoryRecorrente> StoriesRecorrentes => Set<InstagramStoryRecorrente>();

    private static readonly ValueConverter<DateTime, DateTime> DataHoraLocalConverter = new(
        valor => DateTime.SpecifyKind(valor, DateTimeKind.Unspecified),
        valor => DateTime.SpecifyKind(valor, DateTimeKind.Local));

    private static readonly ValueConverter<DateTime?, DateTime?> DataHoraLocalNulaConverter = new(
        valor => valor.HasValue
            ? DateTime.SpecifyKind(valor.Value, DateTimeKind.Unspecified)
            : null,
        valor => valor.HasValue
            ? DateTime.SpecifyKind(valor.Value, DateTimeKind.Local)
            : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var ehSqlite = Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        modelBuilder.Entity<ConfiguracaoMetaOAuth>(entity =>
        {
            entity.ToTable("IgSistema_ConfiguracaoMeta");
            entity.HasKey(e => e.Id);

            if (!ehSqlite)
            {
                entity.Property(e => e.AtualizadoEmUtc)
                    .HasColumnType("timestamp with time zone");
            }
        });

        modelBuilder.Entity<InstagramContaCadastro>(entity =>
        {
            entity.ToTable("IgSistema_ContasInstagram");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IdentificadorInstagram);

            if (!ehSqlite)
            {
                entity.Property(e => e.UltimoTesteEmUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.CriadoEmUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.AtualizadoEmUtc)
                    .HasColumnType("timestamp with time zone");
            }
        });

        modelBuilder.Entity<UsuarioAcesso>(entity =>
        {
            entity.ToTable("IgSistema_UsuariosAcesso");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Login).IsUnique();

            if (!ehSqlite)
            {
                entity.Property(e => e.CriadoEmUtc)
                    .HasColumnType("timestamp with time zone");
            }
        });

        modelBuilder.Entity<InstagramPostAgendado>(entity =>
        {
            entity.ToTable("IgSistema_PostsAgendados");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstagramContaId, e.Status, e.HorarioProgramadoUtc });

            if (!ehSqlite)
            {
                entity.Property(e => e.HorarioProgramadoLocal)
                    .HasColumnType("timestamp without time zone")
                    .HasConversion(DataHoraLocalConverter);
                entity.Property(e => e.HorarioProgramadoUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.CriadoEmUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.PublicadoEmUtc)
                    .HasColumnType("timestamp with time zone");
            }
        });

        modelBuilder.Entity<InstagramStoryRecorrente>(entity =>
        {
            entity.ToTable("IgSistema_StoriesRecorrentes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstagramContaId, e.Ativo, e.ProximoDisparoUtc });

            if (!ehSqlite)
            {
                entity.Property(e => e.DataHoraUnicaLocal)
                    .HasColumnType("timestamp without time zone")
                    .HasConversion(DataHoraLocalNulaConverter);
                entity.Property(e => e.ProximoDisparoLocal)
                    .HasColumnType("timestamp without time zone")
                    .HasConversion(DataHoraLocalConverter);
                entity.Property(e => e.ProximoDisparoUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.UltimaPublicacaoEmUtc)
                    .HasColumnType("timestamp with time zone");
                entity.Property(e => e.CriadoEmUtc)
                    .HasColumnType("timestamp with time zone");
            }
        });

        if (ehSqlite)
        {
            var converter = new ValueConverter<DateTimeOffset, DateTime>(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

            var nullableConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
                v => v.HasValue ? v.Value.UtcDateTime : null,
                v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(converter);
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableConverter);
                    }
                }
            }
        }
    }
}
