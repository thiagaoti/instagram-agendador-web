using InstagramAgendador.Web.Data;
using InstagramAgendador.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddRazorPages();

var rawPostgresConnectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__POSTGRES")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL");

var postgresConnectionString = NormalizarConnectionStringNeon(rawPostgresConnectionString);

var sqliteConnectionString =
    builder.Configuration.GetConnectionString("Sqlite")
    ?? "Data Source=instagram-agendador.db";

builder.Services.AddDbContextFactory<AgendadorDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        options.UseNpgsql(postgresConnectionString);
    }
    else
    {
        options.UseSqlite(sqliteConnectionString);
    }
});

builder.Services
    .AddDataProtection()
    .SetApplicationName("InstagramAgendador.Web")
    .PersistKeysToDbContext<AgendadorDbContext>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "InstagramAgendador.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(90);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSingleton<InstagramGraphApiService>();
builder.Services.AddSingleton<InstagramAgendadorService>();
builder.Services.AddHostedService<InstagramAgendadorWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var agendadorService = scope.ServiceProvider.GetRequiredService<InstagramAgendadorService>();
    await agendadorService.GarantirBancoInicializadoAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/ping", () => Results.Ok(new
{
    status = "alive",
    system = "AgendadorPro",
    horaLocal = FusoHorarioService.AgoraLocal.ToString("yyyy-MM-dd HH:mm:ss")
})).AllowAnonymous();

app.MapGet("/healthz", async (
    IDbContextFactory<AgendadorDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    var ok = await db.Database.CanConnectAsync(cancellationToken);
    return ok
        ? Results.Ok(new { status = "ok", system = "InstagramAgendador.Web" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapGet("/media/post/{id:int}.{ext}", async (
    int id,
    string ext,
    IDbContextFactory<AgendadorDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    var post = await db.PostsAgendados
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    if (post?.ConteudoImagem is null || post.ConteudoImagem.Length == 0)
    {
        return Results.NotFound();
    }

    var contentType = string.IsNullOrWhiteSpace(post.ContentTypeImagem)
        ? (string.Equals(ext, "mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "image/jpeg")
        : post.ContentTypeImagem;

    return Results.File(post.ConteudoImagem, contentType, enableRangeProcessing: true);
}).AllowAnonymous();

app.MapGet("/media/story/{id:int}.{ext}", async (
    int id,
    string ext,
    IDbContextFactory<AgendadorDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    var story = await db.StoriesRecorrentes
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    if (story is null || story.ConteudoImagem.Length == 0)
    {
        return Results.NotFound();
    }

    var contentType = string.IsNullOrWhiteSpace(story.ContentTypeImagem)
        ? (string.Equals(ext, "mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "image/jpeg")
        : story.ContentTypeImagem;

    return Results.File(story.ConteudoImagem, contentType, enableRangeProcessing: true);
}).AllowAnonymous();

app.MapGet("/media/carrossel/{id:int}.{ext}", async (
    int id,
    string ext,
    IDbContextFactory<AgendadorDbContext> dbFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    var item = await db.MidiasPost
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    if (item is null || item.Conteudo.Length == 0)
    {
        return Results.NotFound();
    }

    var contentType = string.IsNullOrWhiteSpace(item.ContentType)
        ? (string.Equals(ext, "mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "image/jpeg")
        : item.ContentType;

    return Results.File(item.Conteudo, contentType, enableRangeProcessing: true);
}).AllowAnonymous();

app.MapPost("/api/meta/descobrir-contas", async (
    HttpContext httpContext,
    InstagramGraphApiService graphApiService,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(
            httpContext.Request.Body,
            cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("accessToken", out var tProp) ? tProp.GetString() : null;
        var appId = root.TryGetProperty("appId", out var idProp) ? idProp.GetString() : null;
        var appSecret = root.TryGetProperty("appSecret", out var secProp) ? secProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(appId))
        {
            appId = configuration["Meta:AppId"] ?? Environment.GetEnvironmentVariable("META__APPID");
        }
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            appSecret = configuration["Meta:AppSecret"] ?? Environment.GetEnvironmentVariable("META__APPSECRET");
        }

        var contas = await graphApiService.DescobrirContasPorTokenAsync(
            accessToken ?? string.Empty,
            appId,
            appSecret,
            cancellationToken);

        return Results.Ok(new
        {
            sucesso = true,
            contas
        });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.BadRequest(new
        {
            sucesso = false,
            erro = ex.GetBaseException().Message
        });
    }
});

app.MapRazorPages();

app.Run();

static string? NormalizarConnectionStringNeon(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return null;
    }

    var valor = connectionString.Trim();
    if (!valor.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !valor.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return valor;
    }

    if (!Uri.TryCreate(valor, UriKind.Absolute, out var uri))
    {
        return valor;
    }

    var userInfo = uri.UserInfo.Split(':', 2);
    var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = uri.AbsolutePath.Trim('/');
    var port = uri.Port > 0 ? uri.Port : 5432;

    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}
