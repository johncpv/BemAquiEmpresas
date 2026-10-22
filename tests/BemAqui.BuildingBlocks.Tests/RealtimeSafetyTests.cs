using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Persistence;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>PRD-02 CA 18: o token do SignalR (query access_token) nunca aparece no log; CA 12: avisos só depois do commit.</summary>
public class RealtimeSafetyTests
{
    private sealed class MemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private const string Token = "eyJhbGciOiJIUzI1NiJ9.segredo-do-hub.assinatura";

    private static string Rendered(MemorySink sink) =>
        string.Join('\n', sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(';', e.Properties.Values)));

    [Fact]
    public void Access_token_da_query_vira_mascara_em_qualquer_propriedade()
    {
        var sink = new MemorySink();
        using var logger = new LoggerConfiguration().Enrich.With<AccessTokenQueryMask>().Enrich.With<SensitivePropertyMask>().WriteTo.Sink(sink).CreateLogger();

        logger.Information("Request starting {Protocol} {Method} {Scheme}://{Host}{Path}{QueryString}", "HTTP/1.1", "GET", "https", "a.localhost", "/hubs/production",
            "?id=abc&access_token=" + Token);
        logger.Information("Falha em {Url}", "https://a.localhost/hubs/production?access_token=" + Token + "&transport=webSockets");

        var text = Rendered(sink);
        text.Should().NotContain(Token).And.Contain("access_token=***").And.Contain("transport=webSockets").And.Contain("id=abc");
    }

    [Theory]
    [InlineData("/hubs/production?access_token=abc", "/hubs/production?access_token=***")]
    [InlineData("?ACCESS_TOKEN=abc&x=1", "?ACCESS_TOKEN=***&x=1")]
    [InlineData("/api/v1/production/board?late=true", "/api/v1/production/board?late=true")]
    public void Mascara_troca_so_o_valor(string input, string expected) => AccessTokenQueryMask.Mask(input).Should().Be(expected);

    [Fact]
    public async Task Aviso_sai_so_no_commit_da_transacao_dele()
    {
        var actions = new AfterCommitActions();
        var sent = new List<string>();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        actions.Add(t1, _ => Add(sent, "t1"));
        actions.Add(t2, _ => Add(sent, "t2"));
        actions.Add(null, _ => Add(sent, "sem transação"));

        await actions.FlushAsync(t2);

        sent.Should().Equal("t2", "sem transação");
        actions.Count.Should().Be(0, "o aviso da transação t1, que não fez commit, é descartado");
    }

    [Fact]
    public async Task Rollback_descarta_e_falha_do_aviso_nao_propaga()
    {
        var actions = new AfterCommitActions();
        var sent = new List<string>();
        var tx = Guid.NewGuid();
        actions.Add(tx, _ => Add(sent, "desfeito"));
        actions.Discard(tx);
        actions.Add(null, _ => throw new InvalidOperationException("hub fora do ar"));
        actions.Add(null, _ => Add(sent, "depois da falha"));

        await actions.Invoking(a => a.FlushAsync(null)).Should().NotThrowAsync();

        sent.Should().Equal("depois da falha");
    }

    private static Task Add(List<string> sent, string value)
    {
        sent.Add(value);
        return Task.CompletedTask;
    }
}
