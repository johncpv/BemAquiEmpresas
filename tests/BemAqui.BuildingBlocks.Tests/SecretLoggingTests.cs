using BemAqui.BuildingBlocks.Logging;
using BemAqui.SharedKernel;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-11 CA 6: segredo nunca aparece no Serilog.</summary>
public class SecretLoggingTests
{
    private sealed class MemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed record ConfigureSmtp(string Host, Secret Password);

    private static (Logger Logger, MemorySink Sink) CreateLogger()
    {
        var sink = new MemorySink();
        var logger = new LoggerConfiguration().Destructure.With<SecretDestructuringPolicy>().WriteTo.Sink(sink).CreateLogger();
        return (logger, sink);
    }

    private static string Rendered(MemorySink sink) =>
        string.Join('\n', sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(';', e.Properties.Values)));

    [Fact]
    public void Segredo_dentro_de_objeto_destruturado_vira_mascara()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Configurando {@Command}", new ConfigureSmtp("smtp.konebras.com.br", new Secret("senha-smtp-real")));

        Rendered(sink).Should().NotContain("senha-smtp-real").And.Contain("***").And.Contain("smtp.konebras.com.br");
    }

    [Fact]
    public void Segredo_como_propriedade_simples_vira_mascara()
    {
        var (logger, sink) = CreateLogger();

        logger.Information("Chave {Key}", new Secret("sk-ant-real"));

        Rendered(sink).Should().NotContain("sk-ant-real").And.Contain("***");
    }

    [Fact]
    public void Segredo_em_excecao_nao_vaza()
    {
        var (logger, sink) = CreateLogger();
        var secret = new Secret("totp-seed-real");

        logger.Error(new InvalidOperationException($"falha com {secret}"), "Erro {Secret}", secret);

        Rendered(sink).Should().NotContain("totp-seed-real");
        sink.Events.Single().Exception!.Message.Should().NotContain("totp-seed-real");
    }
}
