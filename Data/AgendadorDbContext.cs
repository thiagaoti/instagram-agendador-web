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
    public DbSet<InstagramContaCadastro> ContasInstagram => Set<InstagramContaCadastro>();
    public DbSet<UsuarioAcesso> UsuariosAcesso => Set<UsuarioAcesso>();
    public DbSet<InstagramPostAgendado> PostsAgendados => Set<InstagramPostAgendado>();
    public DbSet<InstagramStoryRecorrente> StoriesRecorrentes => Set<InstagramStoryRecorrente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InstagramContaCadastro>(entity =>
        {
            entity.ToTable("IgSistema_ContasInstagram");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IdentificadorInstagram);
        });

        modelBuilder.Entity<UsuarioAcesso>(entity =>
        {
            entity.ToTable("IgSistema_UsuariosAcesso");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Login).IsUnique();
        });

        modelBuilder.Entity<InstagramPostAgendado>(entity =>
        {
            entity.ToTable("IgSistema_PostsAgendados");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstagramContaId, e.Status, e.HorarioProgramadoUtc });
        });

        modelBuilder.Entity<InstagramStoryRecorrente>(entity =>
        {
            entity.ToTable("IgSistema_StoriesRecorrentes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstagramContaId, e.Ativo, e.ProximoDisparoUtc });
        });

        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
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
