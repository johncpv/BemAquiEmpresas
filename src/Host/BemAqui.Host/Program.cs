using BemAqui.Host;
using BemAqui.Host.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("catalog-db", tags: ["ready"]);

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

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
