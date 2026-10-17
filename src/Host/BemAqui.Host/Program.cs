using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Host;
using BemAqui.Host.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

// Logger de bootstrap: erro antes do Serilog configurado (ex.: certificado do key ring ausente) ainda vai para o log.
Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture).CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog(lc => lc
        .ReadFrom.Configuration(builder.Configuration)
        .Destructure.With<SecretDestructuringPolicy>()
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services
        .AddHealthChecks()
        .AddCheck<PostgresHealthCheck>("catalog-db", tags: ["ready"]);

    // Key ring no catálogo protegido por certificado; o Host não sobe sem ele (ADR 0007).
    builder.Services.AddBemAquiSecrets(builder.Configuration, builder.Environment);

    foreach (var module in ModuleRegistry.All)
    {
        module.AddServices(builder.Services, builder.Configuration);
    }

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI só fora de produção (ADR 0009 / revisão §2.3).
        app.MapOpenApi();
    }

    // Vivo: o processo responde. Pronto: dependências (banco) acessíveis.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

    var api = app.MapGroup("/api/v1");
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

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
