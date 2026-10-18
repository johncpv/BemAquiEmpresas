using System.Net;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.Host.Observability;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.Integration.Tests.Observability;

public sealed class MemorySink : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];

    public void Emit(LogEvent logEvent)
    {
        lock (Events)
        {
            Events.Add(logEvent);
        }
    }
}

public sealed class CapturingTransport : IEmailTransport
{
    public List<OutgoingEmail> Sent { get; } = [];

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        Sent.Add(email);
        return Task.CompletedTask;
    }
}

public sealed class CapturingAlertSink : IAlertSink
{
    public List<Alert> Raised { get; } = [];

    public Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        Raised.Add(alert);
        return Task.CompletedTask;
    }
}

public sealed class ProductionEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;

    public string ApplicationName { get; set; } = "BemAqui.Tests";

    public string ContentRootPath { get; set; } = Path.GetTempPath();

    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}

/// <summary>FND-12 CA 1, 4, 5, 6 e 9.</summary>
public class ObservabilityTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Log_do_request_tem_tenant_usuario_correlacao_caminho_status_e_tempo()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "a.localhost";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await tenancy.TokenAsync(tenancy.TenantA, "a.localhost"));

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        LogEvent? request = null;
        for (var i = 0; i < 20 && request is null; i++)
        {
            lock (sink.Events)
            {
                request = sink.Events.LastOrDefault(e => e.Properties.TryGetValue("RequestPath", out var p) && p.ToString().Contains("/api/v1/me", StringComparison.Ordinal));
            }

            if (request is null)
            {
                await Task.Delay(100);
            }
        }

        request.Should().NotBeNull("o request precisa gerar a linha de log");

        request!.Properties["TenantId"].ToString().Should().Contain(tenancy.TenantA.Id.ToString());
        request.Properties["UserId"].ToString().Should().NotBe("null");
        request.Properties["CorrelationId"].ToString().Should().NotBeNullOrWhiteSpace();
        request.Properties["StatusCode"].ToString().Should().Be("200");
        request.Properties.Should().ContainKey("Elapsed");
    }

    [Fact]
    public async Task Ready_responde_503_com_ClamAV_fora_e_live_continua_200()
    {
        await using var factory = tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Health:ClamAv", "true");
            b.UseSetting("ClamAV:Host", "127.0.0.1");
            b.UseSetting("ClamAV:Port", "1");
        });
        var client = factory.CreateClient();

        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        (await live.Content.ReadAsStringAsync()).Should().Be("Healthy", "sem detalhe dos componentes");
    }

    [Fact]
    public async Task Alerta_igual_dentro_da_janela_e_enviado_uma_vez_so()
    {
        var transport = new CapturingTransport();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Alerts:Recipients:0", "superadmin@bemaquiapp.com.br");
            b.ConfigureTestServices(s => s.AddSingleton<IEmailTransport>(transport));
        });
        var alerts = factory.Services.GetRequiredService<IAlertSink>();
        var tenant = "dedup-" + Guid.NewGuid().ToString("N")[..6];

        await alerts.RaiseAsync(new Alert(AlertKinds.JobFailed, tenant, "Job X falhou"));
        await alerts.RaiseAsync(new Alert(AlertKinds.JobFailed, tenant, "Job X falhou de novo"));
        await alerts.RaiseAsync(new Alert(AlertKinds.JobFailed, tenant + "-outro", "Job X falhou"));

        transport.Sent.Should().HaveCount(2, "um por tipo e tenant na janela de 30 min");
        transport.Sent[0].To.Should().Be("superadmin@bemaquiapp.com.br");
        transport.Sent[0].Content.Subject.Should().Contain("Job X falhou");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PlatformAlerts.SingleAsync(a => a.Key == AlertKinds.JobFailed + ":" + tenant)).Occurrences.Should().Be(2);
    }

    [Fact]
    public void Log_em_arquivo_e_JSON_com_rotacao_configurada()
    {
        var dir = Directory.CreateTempSubdirectory("bemaqui-logs-").FullName;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:File:Path"] = Path.Combine(dir, "api-.json"),
            ["Logging:File:RetainedDays"] = "30",
        }).Build();
        var environment = new ProductionEnvironment();

        using (var logger = LoggingSetup.Configure(new LoggerConfiguration(), configuration, environment).CreateLogger())
        {
            logger.Information("teste de rotação {Password}", "nao-pode-aparecer");
        }

        var file = Directory.GetFiles(dir).Should().ContainSingle().Which;
        Path.GetFileName(file).Should().MatchRegex(@"^api-\d{8}\.json$", "rotação diária");
        var line = File.ReadAllText(file);
        line.Should().Contain("\"@t\"").And.Contain("\"@m\"").And.NotContain("nao-pode-aparecer");
    }
}
