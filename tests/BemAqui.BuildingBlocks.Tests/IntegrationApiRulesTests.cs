using BemAqui.BuildingBlocks.Integration;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>INT-03: máscaras da API de integração (CA 7 e 8) e os limites das buscas de cliente (CA 9), com relógio próprio.</summary>
public class IntegrationApiRulesTests
{
    [Theory]
    [InlineData("12345678909", "***.456.789-**")]
    [InlineData("123.456.789-09", "***.456.789-**")]
    [InlineData("11222333000181", "**.222.333/0001-**")]
    [InlineData(null, null)]
    public void Documento_sai_mascarado(string? document, string? expected) => IntegrationData.MaskDocument(document).Should().Be(expected);

    [Theory]
    [InlineData("Maria da Silva Oliveira", "Maria S. O.")]
    [InlineData("João", "João")]
    [InlineData("  ana   paula de souza ", "ana P. S.")]
    public void Nome_abreviado_tem_o_primeiro_nome_e_as_iniciais(string name, string expected) => IntegrationData.AbbreviateName(name).Should().Be(expected);

    [Theory]
    [InlineData("+5511999998888", "11999998888")]
    [InlineData("+551133334444", "1133334444")]
    [InlineData("11999998888", "11999998888")]
    [InlineData("+1 415 555 0100", null)]
    [InlineData("123", null)]
    public void Telefone_E164_vira_o_nacional_do_cadastro(string phone, string? expected) => IntegrationData.NationalPhone(phone).Should().Be(expected);

    [Fact]
    public void Hash_do_termo_nao_carrega_o_termo_e_separa_tenant()
    {
        var tenant = Guid.NewGuid();
        var hash = IntegrationData.TermHash(tenant, "document", "12345678909");
        hash.Should().HaveLength(32).And.NotContain("12345678909");
        IntegrationData.TermHash(Guid.NewGuid(), "document", "12345678909").Should().NotBe(hash);
    }

    [Fact]
    public void Limite_por_minuto_e_por_conexao_com_retry_after()
    {
        var clock = new ManualClock();
        var guard = Guard(clock, perMinute: 2);
        var (tenant, connection) = (Guid.NewGuid(), Guid.NewGuid());

        guard.Acquire(tenant, connection).Allowed.Should().BeTrue();
        guard.Acquire(tenant, connection).Allowed.Should().BeTrue();
        var refused = guard.Acquire(tenant, connection);
        refused.Allowed.Should().BeFalse();
        refused.RetryAfter.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        guard.Peek(tenant, connection).Allowed.Should().BeFalse();
        guard.Acquire(tenant, Guid.NewGuid()).Allowed.Should().BeTrue("o limite é por conexão");

        clock.Advance(TimeSpan.FromSeconds(61));
        guard.Acquire(tenant, connection).Allowed.Should().BeTrue("a janela virou");
    }

    [Fact]
    public void Teto_diario_e_por_tenant_e_vale_ate_o_dia_seguinte()
    {
        var clock = new ManualClock();
        var guard = Guard(clock, perMinute: 100, dailyCap: 3);
        var tenant = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            guard.Acquire(tenant, Guid.NewGuid()).Allowed.Should().BeTrue();
        }

        var refused = guard.Acquire(tenant, Guid.NewGuid());
        refused.Allowed.Should().BeFalse();
        refused.RetryAfter.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
        guard.Acquire(Guid.NewGuid(), Guid.NewGuid()).Allowed.Should().BeTrue("o teto é do tenant");

        clock.Advance(TimeSpan.FromDays(1));
        guard.Acquire(tenant, Guid.NewGuid()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Anomalia_sai_uma_vez_ao_passar_dos_documentos_distintos_na_hora()
    {
        var clock = new ManualClock();
        var guard = Guard(clock, anomaly: 3);
        var (tenant, connection) = (Guid.NewGuid(), Guid.NewGuid());

        guard.TrackDocument(tenant, connection, "a").Should().BeFalse();
        guard.TrackDocument(tenant, connection, "a").Should().BeFalse("repetido não conta");
        guard.TrackDocument(tenant, connection, "b").Should().BeFalse();
        guard.TrackDocument(tenant, connection, "c").Should().BeFalse();
        guard.TrackDocument(tenant, connection, "d").Should().BeTrue("passou de 3 distintos");
        guard.TrackDocument(tenant, connection, "e").Should().BeFalse("o alerta sai uma vez por hora");

        clock.Advance(TimeSpan.FromHours(1));
        guard.TrackDocument(tenant, connection, "a").Should().BeFalse();
    }

    private static IntegrationLookupGuard Guard(TimeProvider clock, int perMinute = 30, int dailyCap = 2000, int anomaly = 50) =>
        new(new StaticOptions(new IntegrationLookupOptions { LookupPerMinute = perMinute, LookupDailyCap = dailyCap, LookupAnomalyDocumentsPerHour = anomaly }), clock);

    private sealed class StaticOptions(IntegrationLookupOptions value) : IOptionsMonitor<IntegrationLookupOptions>
    {
        public IntegrationLookupOptions CurrentValue => value;

        public IntegrationLookupOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<IntegrationLookupOptions, string?> listener) => null;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
