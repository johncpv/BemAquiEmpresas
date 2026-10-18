using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Observability;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-12 CA 3 e 8: máscara por nome de propriedade e alerta de integração após N falhas seguidas.</summary>
public class ObservabilityTests
{
    private sealed class MemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class CapturingAlerts : IAlertSink
    {
        public List<Alert> Raised { get; } = [];

        public Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            Raised.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed record LoginAttempt(string Email, string Password, Dictionary<string, string> Headers, IReadOnlyList<ApiCredential> Credentials);

    private sealed record ApiCredential(string Provider, string ApiKey);

    [Fact]
    public void Propriedades_sensiveis_viram_mascara_ate_dentro_de_objetos_e_listas()
    {
        var sink = new MemorySink();
        using var logger = new LoggerConfiguration().Enrich.With<SensitivePropertyMask>().WriteTo.Sink(sink).CreateLogger();
        var attempt = new LoginAttempt("ana@konebras.com.br", "senha-real",
            new Dictionary<string, string> { ["Authorization"] = "Bearer eyJ-real", ["Accept"] = "application/json" },
            [new ApiCredential("anthropic", "sk-real")]);

        logger.Information("Login {@Attempt} token {AccessToken} cookie {Cookie}", attempt, "tok-real", "ck-real");

        var rendered = sink.Events.Single().RenderMessage(System.Globalization.CultureInfo.InvariantCulture);
        rendered.Should().NotContain("senha-real").And.NotContain("eyJ-real").And.NotContain("sk-real").And.NotContain("tok-real").And.NotContain("ck-real");
        // E-mail é dado pessoal desde a CAD-01a (CA 25): também sai mascarado.
        rendered.Should().NotContain("ana@konebras.com.br").And.Contain("application/json").And.Contain("***");
    }

    [Theory]
    [InlineData("Password", true)]
    [InlineData("SmtpPassword", true)]
    [InlineData("refresh_token", true)]
    [InlineData("X-Api-Key", true)]
    [InlineData("ConnectionString", true)]
    [InlineData("Email", true)]
    [InlineData("TenantId", false)]
    public void Nome_sensivel_e_reconhecido(string name, bool sensitive) => SensitivePropertyMask.IsSensitive(name).Should().Be(sensitive);

    [Fact]
    public async Task Integracao_alerta_ao_atingir_o_limite_de_falhas_seguidas_e_sucesso_zera()
    {
        var alerts = new CapturingAlerts();
        var tracker = new IntegrationHealthTracker(alerts, Options.Create(new AlertOptions { IntegrationFailureThreshold = 3 }));

        await tracker.RecordFailureAsync("smtp", "konebras", "timeout");
        await tracker.RecordFailureAsync("smtp", "konebras", "timeout");
        tracker.RecordSuccess("smtp");
        await tracker.RecordFailureAsync("smtp", "konebras", "timeout");
        await tracker.RecordFailureAsync("smtp", "konebras", "timeout");
        alerts.Raised.Should().BeEmpty("o sucesso zerou a sequência");

        await tracker.RecordFailureAsync("smtp", "konebras", "timeout");

        alerts.Raised.Should().ContainSingle().Which.Kind.Should().Be(AlertKinds.IntegrationDown);
    }
}
