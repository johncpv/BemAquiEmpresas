using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Lookups;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Host;
using BemAqui.Host.Cli;
using BemAqui.Host.Health;
using BemAqui.Host.Observability;
using BemAqui.Host.Persistence;
using BemAqui.Host.Security;
using BemAqui.Host.Tenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

// Modo CLI da imagem: migrate (antes da subida) e tenant provision (ADR 0006, FND-03a).
if (CliCommand.IsCli(args))
{
    return await CliCommand.RunAsync(args);
}

// Logger de bootstrap: erro antes do Serilog configurado (ex.: certificado do key ring ausente) ainda vai para o log.
Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture).CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddSecretFiles();

    builder.Services.AddSerilog((sp, lc) =>
    {
        LoggingSetup.Configure(lc, builder.Configuration, builder.Environment);
        foreach (var sink in sp.GetServices<Serilog.Core.ILogEventSink>())
        {
            lc.WriteTo.Sink(sink); // sinks extras registrados na DI (ex.: testes)
        }
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler>();
    // Secret entra como texto write-only (ADR 0007): no contrato é string, nunca objeto, e nunca aparece numa resposta.
    builder.Services.AddOpenApi(o => o.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(BemAqui.SharedKernel.Secret))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.String;
            schema.Format = "password";
            schema.WriteOnly = true;
            schema.Properties?.Clear();
        }

        return Task.CompletedTask;
    }));
    builder.Services.AddHttpContextAccessor();
    var health = builder.Configuration.GetSection(HealthOptions.Section).Get<HealthOptions>() ?? new HealthOptions();
    var checks = builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("catalog-db", tags: ["ready"]);
    if (health.ClamAv)
    {
        checks.AddCheck<ClamAvHealthCheck>("clamav", tags: ["ready"]);
    }

    if (health.Storage)
    {
        checks.AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
    }

    if (health.Hangfire && builder.Configuration.GetValue("Jobs:ServerEnabled", true))
    {
        checks.AddCheck<HangfireHealthCheck>("jobs", tags: ["ready"]);
    }

    // Key ring no catálogo protegido por certificado; o Host não sobe sem ele (ADR 0007).
    builder.Services.AddBemAquiSecrets(builder.Configuration, builder.Environment);

    // Jobs com storage no catálogo (schema hangfire) e filas separadas; dashboard só no host da plataforma (FND-13).
    builder.Services.AddBemAquiJobs(builder.Configuration);
    if (builder.Configuration.GetValue("Jobs:ServerEnabled", true))
    {
        builder.Services.AddHostedService<RecurringJobsRegistrar>();
    }

    // Tenant pelo host (ADR 0004), banco do tenant por escopo e outbox por tenant ativo (ADR 0006).
    builder.Services.Configure<PlatformHostOptions>(builder.Configuration.GetSection(PlatformHostOptions.Section));
    builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.Section));
    builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.Section));
    builder.Services.Configure<AttachmentOptions>(builder.Configuration.GetSection(AttachmentOptions.Section));
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddTenantPersistence();
    builder.Services.AddSingleton<BemAqui.Platform.Application.Provisioning.ITenantDatabaseMigrator, TenantDatabaseMigrator>();
    builder.Services.AddBemAquiEmail(builder.Configuration);
    builder.Services.AddBemAquiLookups(builder.Configuration);
    builder.Services.AddBemAquiMessaging(builder.Configuration, HostEvents.Register);

    // Endurecimento HTTP (FND-02b): proxy confiável, JWT preso ao tenant e ao host, SignalR. Sem CORS (mesma origem).
    var proxy = builder.Configuration.GetSection(ReverseProxyOptions.Section).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();
    builder.Services.Configure<ReverseProxyOptions>(builder.Configuration.GetSection(ReverseProxyOptions.Section));
    builder.Services.Configure<ForwardedHeadersOptions>(o => ReverseProxySetup.Configure(o, proxy));
    builder.Services.AddTenantJwtAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddSignalR();
    builder.Services.AddAuthRateLimiting(builder.Configuration);
    // Número é número no contrato (OpenAPI/Orval): sem aceitar "1" como 1, que gerava tipos integer|string no cliente.
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
    PermissionResultHandler.Register(builder.Services);
    builder.Services.AddSingleton(BemAqui.BuildingBlocks.Help.HelpCatalog.FromAssembly(typeof(Program).Assembly)); // ajuda inválida derruba a subida

    foreach (var module in ModuleRegistry.All)
    {
        module.AddServices(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    app.UseMiddleware<TrustedProxyMiddleware>();
    app.UseForwardedHeaders();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseRequestLogContext();
    app.UseBemAquiRequestLogging();
    app.UsePlatformDashboard(); // antes da policy padrão: o painel tem a própria autorização (esquema Platform)
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI só fora de produção (ADR 0009 / revisão §2.3).
        app.MapOpenApi().AllowAnonymous();
    }
    else
    {
        // Fora de Development a documentação não existe (FND-02 CA 12): 404, não 401.
        app.MapGet("/openapi/{**rest}", () => Results.NotFound()).AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/swagger/{**rest}", () => Results.NotFound()).AllowAnonymous().ExcludeFromDescription();
    }

    // Vivo: o processo responde. Pronto: dependências (banco) acessíveis.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    app.MapHub<PingHub>(PingHub.Path);

    var api = app.MapGroup("/api/v1");
    api.MapAuditEndpoints();
    api.MapAttachmentEndpoints();
    api.MapNotificationEndpoints();
    api.MapModuleEndpoints();
    foreach (var module in ModuleRegistry.All)
    {
        module.MapEndpoints(api);
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "BemAqui Empresas não subiu: {Motivo}", ex.Message);
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
